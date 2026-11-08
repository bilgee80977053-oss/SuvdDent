using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Demo;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DemoPreparationTests
{
    private const string PatientPassword="Synthetic-demo-patient-password-2026";
    private const string DirectorPassword="Synthetic-demo-director-password-2026";
    private static DemoCredentials Credentials() => new(PatientPassword,DirectorPassword);
    private static string Root() => Path.Combine(Path.GetTempPath(),"NexoraDemoTests-"+Guid.NewGuid().ToString("N"),"NexoraDental-Demos");
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
    private static AppDbContext Open(string path) => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(DemoDatabaseSafety.ConnectionString(path)).Options);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static async Task<DemoManifest> Manifest(string path) => JsonSerializer.Deserialize<DemoManifest>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!,DemoDatabaseSafety.ManifestFile)))!;

    private sealed class DemoApiFactory(string root,string path) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureAppConfiguration((_,config)=>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string,string?>
                {
                    ["Demo:Root"]=root,["Demo:DatabasePath"]=path,
                    // Deliberately dangerous fallback; Demo must ignore it entirely.
                    ["ConnectionStrings:Default"]="Data Source=DO-NOT-CREATE-DEFAULT.db",
                    ["Jwt:Key"]="Synthetic-demo-clinic-test-key-01234567890123456789",
                    ["Jwt:Issuer"]="DemoClinicTests",["Jwt:Audience"]="DemoStaffTests",["Jwt:ExpireHours"]="1",
                    ["PlatformJwt:Key"]="Synthetic-demo-platform-test-key-98765432109876543210",
                    ["PlatformJwt:Issuer"]="DemoPlatformTests",["PlatformJwt:Audience"]="DemoPatientTests",
                    ["DemoNotifications:Enabled"]="false"
                });
            });
            builder.ConfigureServices(services=>
            {
                foreach(var descriptor in services.Where(x=>x.ImplementationType==typeof(DemoNotificationWorker)).ToArray())
                    services.Remove(descriptor);
            });
        }
    }

    [Fact]
    public async Task Prepared_dataset_supports_real_JWT_login_new_bookings_and_idempotency_without_startup_migration()
    {
        var root=Root(); var path=await DemoPreparation.PrepareAsync(root,Credentials(),TimeProvider.System);
        var manifest=await Manifest(path);
        string schema;
        await using(var db=Open(path))
        {
            await DemoDatabaseSafety.ValidateReadyAsync(db,root,path);
            schema=await DemoDatabaseSafety.SchemaHashAsync(db);
            Assert.Equal(1,await db.Clinics.CountAsync()); Assert.Equal(4,await db.Users.CountAsync());
            Assert.Equal(3,await db.DoctorProfiles.CountAsync()); Assert.Equal(5,await db.Services.CountAsync());
            Assert.Equal(7,await db.DoctorServices.CountAsync()); Assert.Equal(30,await db.DoctorWorkingHours.CountAsync());
            Assert.Equal(1,await db.PlatformAccounts.CountAsync()); Assert.Equal(1,await db.Patients.CountAsync());
            Assert.Equal(1,await db.ClinicPatientLinks.CountAsync()); Assert.Equal(1,await db.Appointments.CountAsync());
            var notifications=await db.AppointmentNotificationOutbox.ToArrayAsync(); Assert.Equal(2,notifications.Length);
            Assert.All(notifications,x=>{Assert.Equal("DemoGenerated",x.Status);Assert.StartsWith("DEMO",x.Body);});
            Assert.All(await db.Users.ToArrayAsync(),u=>{Assert.EndsWith(".invalid",u.Email);Assert.Null(u.Phone);});
            var account=await db.PlatformAccounts.SingleAsync(); Assert.Null(account.Phone); Assert.True(account.IsEmailVerified);
            Assert.True(BCrypt.Net.BCrypt.Verify(PatientPassword,account.PasswordHash));
        }
        using var app=new DemoApiFactory(root,path);
        using var client=app.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
        using var anonymous=await client.GetAsync("/api/platform/me/appointments"); Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var invalid=await client.PostAsJsonAsync("/api/platform/auth/login",new{identifier="patient@nexora.example.invalid",password="incorrect"});
        Assert.Equal(HttpStatusCode.Unauthorized,invalid.StatusCode);
        using var login=await client.PostAsJsonAsync("/api/platform/auth/login",new{identifier="patient@nexora.example.invalid",password=PatientPassword});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var identity=(await login.Content.ReadFromJsonAsync<PlatformLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",identity.Token);
        foreach(var slot in manifest.CandidateSlots.Take(3))
        {
            var o=(await client.GetFromJsonAsync<BookingOptionsResponse>($"/api/platform/clinics/{manifest.ClinicId}/booking-options?doctorProfileId={slot.DoctorProfileId}&serviceId={slot.ServiceId}"))!;
            var body=new BookAppointmentRequest(slot.DoctorProfileId,slot.ServiceId,slot.StartUtc,o.ClinicVersion,o.DoctorVersion,false);
            var key=Guid.NewGuid();
            async Task<HttpResponseMessage> Book()
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/platform/clinics/{manifest.ClinicId}/appointments");
                request.Headers.Add("Idempotency-Key",key.ToString()); request.Content=JsonContent.Create(body);
                return await client.SendAsync(request);
            }
            using var booked=await Book(); using var replay=await Book();
            Assert.Equal(HttpStatusCode.Created,booked.StatusCode);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            Assert.Equal(await booked.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }
        await using(var db=Open(path))
        {
            Assert.Equal(schema,await DemoDatabaseSafety.SchemaHashAsync(db));
            Assert.Equal(1,await db.Clinics.CountAsync()); Assert.Equal(4,await db.Appointments.CountAsync());
            Assert.Equal(8,await db.AppointmentNotificationOutbox.CountAsync());
        }
        using var scope=app.Services.CreateScope();
        Assert.IsType<DisabledPlatformMessageSender>(scope.ServiceProvider.GetRequiredService<IPlatformMessageSender>());
        var serviceDb=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(path,new SqliteConnectionStringBuilder(serviceDb.Database.GetConnectionString()).DataSource);
        var smtpConfig=new ConfigurationBuilder().AddConfiguration(scope.ServiceProvider.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string,string?> { ["Email:Host"]="smtp.example.invalid" }).Build();
        var smtpError=Assert.Throws<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateRuntimeConfiguration(smtpConfig));
        Assert.Equal("SMTP configuration is not permitted in DEMO.",smtpError.Message);
        using var disabledRegister=await client.PostAsJsonAsync("/api/platform/auth/register",new
        { fullName="Synthetic disabled registration",email="another@nexora.example.invalid",password=PatientPassword });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,disabledRegister.StatusCode);
        using var directorClient=app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost") });
        using var directorLogin=await directorClient.PostAsJsonAsync("/api/Auth/login",new
        { identifier="director@nexora.example.invalid",password=DirectorPassword,clinicId=manifest.ClinicId });
        Assert.Equal(HttpStatusCode.OK,directorLogin.StatusCode);
        using var directorIdentity=JsonDocument.Parse(await directorLogin.Content.ReadAsStringAsync());
        directorClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",directorIdentity.RootElement.GetProperty("token").GetString());
        using var catalog=await directorClient.GetAsync("/api/Doctors"); Assert.Equal(HttpStatusCode.OK,catalog.StatusCode);
        using var catalogJson=JsonDocument.Parse(await catalog.Content.ReadAsStringAsync()); Assert.Equal(3,catalogJson.RootElement.GetArrayLength());
        using var wrongScheme=await directorClient.GetAsync("/api/platform/me/appointments"); Assert.Equal(HttpStatusCode.Unauthorized,wrongScheme.StatusCode);
        var raw=await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!,DemoDatabaseSafety.ManifestFile));
        Assert.DoesNotContain(PatientPassword,raw); Assert.DoesNotContain(DirectorPassword,raw);
        Assert.DoesNotContain("Token",raw); Assert.DoesNotContain("Password",raw); Assert.Equal(9,manifest.CandidateSlots.Length);
    }

    [Theory]
    [InlineData("2026-11-06T15:59:00Z","2026-11-09")]
    [InlineData("2026-11-07T16:01:00Z","2026-11-09")]
    [InlineData("2026-11-08T16:01:00Z","2026-11-10")]
    public async Task Future_working_days_use_clinic_timezone_and_skip_weekends(string now,string expected)
    {
        var path=await DemoPreparation.PrepareAsync(Root(),Credentials(),new Clock(DateTimeOffset.Parse(now,CultureInfo.InvariantCulture)));
        var manifest=await Manifest(path); var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar");
        Assert.Equal(expected,TimeZoneInfo.ConvertTime(manifest.SampleStartUtc,zone).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        Assert.All(manifest.CandidateSlots,x=>
        {
            Assert.True(x.StartUtc>manifest.SampleStartUtc);
            Assert.DoesNotContain(TimeZoneInfo.ConvertTime(x.StartUtc,zone).DayOfWeek,new[]{DayOfWeek.Saturday,DayOfWeek.Sunday});
        });
    }

    [Fact]
    public async Task Preparing_again_uses_a_new_database_and_never_changes_existing_files()
    {
        var root=Root();var first=await DemoPreparation.PrepareAsync(root,Credentials(),TimeProvider.System);
        var hash=Hash(first);var second=await DemoPreparation.PrepareAsync(root,Credentials(),TimeProvider.System);
        Assert.NotEqual(first,second);Assert.Equal(hash,Hash(first));
        await using var db=Open(first);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new DemoDataSeeder(db,new ClinicLocationService(db,new MongoliaLocations()),
            new AppointmentService(db,new ClinicSchedulingPolicy(db,new MongoliaLocations()),TimeProvider.System,new AppointmentConfirmationNotifications(db)),TimeProvider.System)
            .SeedAsync(Credentials(),Path.GetFileName(Path.GetDirectoryName(first))!));
        Assert.Equal(hash,Hash(first));
    }

    [Theory]
    [InlineData("DROP TRIGGER TR_Appointments_NoOverlap_INSERT")]
    [InlineData("DROP TABLE DoctorWorkingHours")]
    [InlineData("DELETE FROM __EFMigrationsHistory WHERE MigrationId='20261108120446_AddAppointmentNotificationOutbox'")]
    public async Task Incomplete_new_test_schemas_are_rejected_and_never_repaired(string damage)
    {
        // These are brand-new disposable fictional DEMO files, never a development/production DB.
        var root=Root();var path=await DemoPreparation.PrepareAsync(root,Credentials(),TimeProvider.System);
        await using(var db=Open(path)) await db.Database.ExecuteSqlRawAsync(damage);
        var hash=Hash(path);
        await using(var db=Open(path)) await Assert.ThrowsAsync<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateReadyAsync(db,root,path));
        Assert.Equal(hash,Hash(path));
        using var app=new DemoApiFactory(root,path);
        Assert.ThrowsAny<Exception>(()=>app.CreateClient());
        Assert.Equal(hash,Hash(path));
    }

    [Fact]
    public void Unexpected_paths_and_missing_manifests_are_rejected()
    {
        var root=Root();
        Assert.Throws<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateRoot("relative/NexoraDental-Demos"));
        Assert.Throws<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateRoot(Path.GetTempPath()));
        Assert.Throws<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateDatabasePath(root,Path.Combine(root,"dental.db"),false));
        var candidate=Path.Combine(root,"demo-20261108-000000-"+Guid.NewGuid().ToString("N"),DemoDatabaseSafety.DatabaseFile);
        Assert.Throws<InvalidOperationException>(()=>DemoDatabaseSafety.ValidateDatabasePath(root,candidate,true));
        Assert.False(File.Exists(candidate));
    }

    [Fact]
    public async Task Invalid_passwords_fail_before_creating_any_demo_directory()
    {
        var root=Root();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>DemoPreparation.PrepareAsync(root,new("short",DirectorPassword),TimeProvider.System));
        Assert.False(Directory.Exists(root));
    }
}
