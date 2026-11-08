using System.Text.Json;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalApi.Demo;

public static class DemoPreparation
{
    public static async Task<int> RunCommandAsync(string[] args)
    {
        try
        {
            if (args.Length != 1 || args[0] != "--prepare-demo") throw new InvalidOperationException("Invalid DEMO command.");
            var root=Environment.GetEnvironmentVariable("NEXORA_DEMO_ROOT");
            var credentials=new DemoCredentials(Environment.GetEnvironmentVariable("NEXORA_DEMO_PATIENT_PASSWORD") ?? "",
                Environment.GetEnvironmentVariable("NEXORA_DEMO_DIRECTOR_PASSWORD") ?? "");
            var result=await PrepareAsync(root!,credentials,TimeProvider.System);
            Console.WriteLine(JsonSerializer.Serialize(new { DatabasePath=result, ManifestPath=Path.Combine(Path.GetDirectoryName(result)!,DemoDatabaseSafety.ManifestFile) }));
            return 0;
        }
        catch (Exception)
        {
            // No exception text, credentials, SQL values or synthetic patient data in output.
            Console.Error.WriteLine("DEMO preparation failed. Check private configuration and paths. Any incomplete directory is retained.");
            return 1;
        }
    }

    public static async Task<string> PrepareAsync(string rootInput, DemoCredentials credentials, TimeProvider clock)
    {
        credentials.Validate();
        var root=DemoDatabaseSafety.ValidateRoot(rootInput);
        Directory.CreateDirectory(root); DemoDatabaseSafety.RejectLinks(root);
        var name="demo-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",System.Globalization.CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N");
        var directory=Path.Combine(root,name);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("DEMO directory already exists.");
        Directory.CreateDirectory(directory);
        var path=DemoDatabaseSafety.ValidateDatabasePath(root,Path.Combine(directory,DemoDatabaseSafety.DatabaseFile),false);
        // CreateNew is the exclusive no-overwrite reservation. No existing DB path can reach MigrateAsync.
        await using (var reservation=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
        DemoDatabaseSafety.RejectLinks(path);
        var services=new ServiceCollection();
        services.AddSingleton(clock);
        services.AddDbContext<AppDbContext>(o=>o.UseSqlite(DemoDatabaseSafety.ConnectionString(path)));
        services.AddSingleton<MongoliaLocations>(); services.AddScoped<ClinicLocationService>();
        services.AddScoped<ClinicSchedulingPolicy>(); services.AddScoped<AppointmentService>();
        services.AddScoped<IAppointmentConfirmationNotifications,AppointmentConfirmationNotifications>();
        services.AddSingleton<IDemoAppointmentNotificationProvider,DemoAppointmentNotificationProvider>();
        services.AddScoped<AppointmentNotificationProcessor>(); services.AddScoped<DemoDataSeeder>();
        using var provider=services.BuildServiceProvider();
        using var scope=provider.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (new FileInfo(path).Length != 0) throw new InvalidOperationException("Reserved DEMO file is not empty.");
        await db.Database.MigrateAsync(); // Only the just-reserved, validated, brand-new file.
        var manifest=await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(credentials,name);
        if (await db.Clinics.CountAsync()!=1 || await db.Users.CountAsync()!=4 || await db.DoctorProfiles.CountAsync()!=3 ||
            await db.Services.CountAsync()!=5 || await db.DoctorServices.CountAsync()!=7 || await db.DoctorWorkingHours.CountAsync()!=30 ||
            await db.PlatformAccounts.CountAsync()!=1 || await db.Patients.CountAsync()!=1 || await db.ClinicPatientLinks.CountAsync()!=1 ||
            await db.Appointments.CountAsync()!=1 || await db.AppointmentNotificationOutbox.CountAsync()!=2)
            throw new InvalidOperationException("DEMO counts are inconsistent.");
        if (await scope.ServiceProvider.GetRequiredService<AppointmentNotificationProcessor>().ProcessBatchAsync()!=2)
            throw new InvalidOperationException("DEMO notification simulation failed.");
        manifest=manifest with { SchemaSha256=await DemoDatabaseSafety.SchemaHashAsync(db) };
        await using (var file=new FileStream(Path.Combine(directory,DemoDatabaseSafety.ManifestFile),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await JsonSerializer.SerializeAsync(file,manifest,new JsonSerializerOptions { WriteIndented=true });
        await DemoDatabaseSafety.ValidateReadyAsync(db,root,path);
        return path;
    }
}
