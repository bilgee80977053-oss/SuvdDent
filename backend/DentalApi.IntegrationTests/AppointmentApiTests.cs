using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class AppointmentApiTests
{
    private const string Url = "/api/appointments";
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FailCommit : DbTransactionInterceptor
    {
        internal bool Enabled;
        internal Exception Error = new SqliteException("synthetic-private-commit-failure", 5);
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw Error;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Factory : DoctorApiFactory
    {
        internal readonly FailCommit Failure = new();
        internal readonly Clock Time = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time);
                services.AddDbContext<AppDbContext>(o => o.AddInterceptors(Failure));
            });
        }
    }
    private static async Task Mutate(Factory app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }
    private static async Task<Factory> App()
    {
        var app = new Factory();
        await app.SeedAsync();
        await Mutate(app, async db =>
        {
            var clinic = await db.Clinics.SingleAsync(c => c.Id == 101);
            clinic.CountryCode = "MN"; clinic.ProvinceOrCapitalCode = "11"; clinic.SoumOrDistrictCode = "1101";
            clinic.AddressLine = "Synthetic address"; clinic.TimeZoneId = "Asia/Ulaanbaatar";
            clinic.TimeZoneStatus = TimeZoneStatus.Verified; clinic.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
            clinic.TimeZoneVerifiedByUserId = 15; clinic.TimeZoneVerifiedClinicId = 101;
            db.Users.Add(new User { Id = 27, ClinicId = 101, Role = Role.Patient, FullName = "Synthetic second patient",
                Email = "user27@example.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4), IsEmailVerified = true });
            db.Patients.Add(new Patient { Id = 43, ClinicId = 101, UserId = 27, FullName = "Synthetic second patient" });
            db.Services.AddRange(new Service { Id = 51, ClinicId = 101, Name = "Synthetic treatment", MinPrice = 100 },
                new Service { Id = 52, ClinicId = 101, Name = "Inactive", IsActive = false },
                new Service { Id = 61, ClinicId = 202, Name = "Other clinic" });
            db.DoctorProfiles.Add(new DoctorProfile { Id = 36, ClinicId = 101, UserId = 20, IsPublished = true });
            await db.SaveChangesAsync();
            foreach (var doctor in new[] { 31, 33, 34, 35, 36 })
            {
                db.DoctorServices.Add(new DoctorService { ClinicId = 101, DoctorProfileId = doctor, ServiceId = 51,
                    DurationMinutes = 30, BufferBeforeMinutes = 5, BufferAfterMinutes = 10 });
                if (doctor != 31) db.DoctorWorkingHours.Add(new DoctorWorkingHours { ClinicId = 101, DoctorProfileId = doctor,
                    DayOfWeek = 1, StartMinute = 540, EndMinute = 780 });
            }
            await db.SaveChangesAsync();
        });
        return app;
    }
    private static CreateAppointmentRequest Request(string start = "2026-11-02T09:15:00+08:00", int doctor = 31,
        int service = 51, Guid? request = null) => new(doctor, service, start, request ?? Guid.NewGuid());
    private static async Task AssertStatus(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.True(response.StatusCode == status, $"Expected {status}; actual {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }
    private static async Task<AppointmentResponse> Book(HttpClient client, CreateAppointmentRequest? request = null)
    {
        using var result = await client.PostAsJsonAsync(Url, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.NotNull(result.Headers.Location);
        return (await result.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }
    private static async Task<int> Count(Factory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Appointments.CountAsync();
    }

    [Fact]
    public async Task Valid_booking_uses_trusted_snapshots_and_UTC_and_is_readable_with_pagination()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var row = await Book(client);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 1, 15, 0, TimeSpan.Zero), row.StartUtc);
        Assert.Equal(TimeSpan.Zero, row.StartUtc.Offset); Assert.Equal(row.StartUtc.AddMinutes(30), row.EndUtc);
        Assert.Equal(row.StartUtc.AddMinutes(-5), row.OccupiedStartUtc); Assert.Equal(row.EndUtc.AddMinutes(10), row.OccupiedEndUtc);
        Assert.Equal("Asia/Ulaanbaatar", row.ClinicTimeZoneId); Assert.Equal("Confirmed", row.Status);
        Assert.Equal(row, await client.GetFromJsonAsync<AppointmentResponse>($"{Url}/{row.Id}"));
        var page = await client.GetFromJsonAsync<AppointmentPage>($"{Url}/me?page=1&pageSize=1");
        Assert.Equal(row, Assert.Single(page!.Items));
        Assert.Empty((await client.GetFromJsonAsync<AppointmentPage>($"{Url}/me?page=2&pageSize=1"))!.Items);
        await AssertStatus(await client.GetAsync($"{Url}/me?page=0"), HttpStatusCode.BadRequest);
        await AssertStatus(await client.GetAsync($"{Url}/me?pageSize=101"), HttpStatusCode.BadRequest);
        await Mutate(app, async db =>
        {
            var stored = await db.Appointments.SingleAsync();
            Assert.Equal(41, stored.PatientId); Assert.Equal(101, stored.ClinicId);
            Assert.Equal(30, stored.DurationMinutesSnapshot); Assert.Equal(5, stored.BufferBeforeMinutesSnapshot);
            Assert.Equal(10, stored.BufferAfterMinutesSnapshot);
        });
    }

    [Fact]
    public async Task Catalog_is_tenant_scoped_active_published_and_safe_and_existing_endpoints_work()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var services = await client.GetFromJsonAsync<BookingServiceResponse[]>($"{Url}/services?clinicId=202");
        Assert.Equal(51, Assert.Single(services!).Id);
        var doctors = await client.GetFromJsonAsync<BookingDoctorResponse[]>($"{Url}/doctors?serviceId=51");
        Assert.Equal(new[] {31, 36}, doctors!.Select(d => d.Id));
        await AssertStatus(await client.GetAsync($"{Url}/doctors?serviceId=61"), HttpStatusCode.NotFound);
        await AssertStatus(await client.GetAsync($"{Url}/doctors?serviceId=0"), HttpStatusCode.BadRequest);
        foreach (var path in new[] { "/api/Doctors", "/api/Patients/me", "/api/Services?clinicId=101" })
            await AssertStatus(await client.GetAsync(path), HttpStatusCode.OK);
        var json = await client.GetStringAsync($"{Url}/doctors?serviceId=51");
        foreach (var hidden in new[] { "email", "password", "phone", "userId", "medicalNotes" })
            Assert.DoesNotContain(hidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 401)] [InlineData(13, 403)] [InlineData(15, 403)] [InlineData(16, 403)]
    public async Task All_routes_require_authenticated_patient(int user, int expected)
    {
        using var app = await App(); using var client = user == 0 ? app.Client() : await app.AsAsync(user);
        foreach (var path in new[] { "/me", "/1", "/services", "/doctors?serviceId=51", "/slots?doctorProfileId=31&serviceId=51&date=2026-11-02" })
            await AssertStatus(await client.GetAsync(Url + path), (HttpStatusCode)expected);
        await AssertStatus(await client.PostAsJsonAsync(Url, Request()), (HttpStatusCode)expected);
        Assert.Equal(0, await Count(app));
    }

    [Theory]
    [InlineData("unverified-email", 403)] [InlineData("empty-email", 403)]
    [InlineData("missing-patient", 404)] [InlineData("inactive-patient", 404)]
    [InlineData("inactive-user", 401)] [InlineData("timezone", 409)]
    [InlineData("verifier", 409)] [InlineData("service", 404)] [InlineData("association", 404)]
    [InlineData("doctor", 404)] [InlineData("doctor-user", 404)] [InlineData("unpublished", 404)]
    public async Task Booking_rechecks_all_eligibility_inside_transaction(string scenario, int expected)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Mutate(app, async db =>
        {
            switch (scenario)
            {
                case "unverified-email": (await db.Users.FindAsync(11))!.IsEmailVerified = false; break;
                case "empty-email": (await db.Users.FindAsync(11))!.Email = " "; break;
                case "missing-patient": db.Patients.Remove((await db.Patients.FindAsync(41))!); break;
                case "inactive-patient": (await db.Patients.FindAsync(41))!.IsActive = false; break;
                case "inactive-user": (await db.Users.FindAsync(11))!.IsActive = false; break;
                case "timezone":
                    var clinic = (await db.Clinics.FindAsync(101))!;
                    clinic.TimeZoneStatus = TimeZoneStatus.PendingVerification;
                    clinic.TimeZoneVerifiedAtUtc = null; clinic.TimeZoneVerifiedByUserId = null; clinic.TimeZoneVerifiedClinicId = null;
                    break;
                case "verifier": (await db.Users.FindAsync(15))!.IsActive = false; break;
                case "service": (await db.Services.FindAsync(51))!.IsActive = false; break;
                case "association": (await db.DoctorServices.SingleAsync(s => s.DoctorProfileId == 31))!.IsActive = false; break;
                case "doctor": (await db.DoctorProfiles.FindAsync(31))!.IsActive = false; break;
                case "doctor-user": (await db.Users.FindAsync(13))!.IsActive = false; break;
                case "unpublished": (await db.DoctorProfiles.FindAsync(31))!.IsPublished = false; break;
            }
            await db.SaveChangesAsync();
        });
        await AssertStatus(await client.PostAsJsonAsync(Url, Request()), (HttpStatusCode)expected);
        Assert.Equal(0, await Count(app));
    }

    [Theory]
    [InlineData(32, 51)] [InlineData(31, 61)] [InlineData(33, 51)] [InlineData(34, 51)] [InlineData(35, 51)]
    public async Task Foreign_or_hidden_resources_are_not_accessible(int doctor, int service)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await AssertStatus(await client.PostAsJsonAsync(Url, Request(doctor:doctor, service:service)), HttpStatusCode.NotFound);
        await AssertStatus(await client.GetAsync($"{Url}/slots?doctorProfileId={doctor}&serviceId={service}&date=2026-11-02"), HttpStatusCode.NotFound);
        Assert.Equal(0, await Count(app));
    }

    [Theory]
    [InlineData("2026-11-02T09:15:00", 400)] [InlineData("invalid", 400)]
    [InlineData("2026-11-02T09:16:00+08:00", 400)] [InlineData("2026-11-02T09:15:00.0000001+08:00", 400)]
    [InlineData("2026-10-31T09:15:00+08:00", 400)] [InlineData("2026-12-02T09:15:00+08:00", 400)]
    [InlineData("2026-11-02T09:00:00+08:00", 409)] [InlineData("2026-11-02T12:30:00+08:00", 409)]
    [InlineData("2026-11-03T09:15:00+08:00", 409)]
    public async Task Invalid_grid_dates_and_working_boundaries_are_rejected(string start, int expected)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await AssertStatus(await client.PostAsJsonAsync(Url, Request(start)), (HttpStatusCode)expected);
        Assert.Equal(0, await Count(app));
    }

    [Fact]
    public async Task Slots_are_actual_buffer_aware_advisory_and_remove_doctor_and_patient_conflicts()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var path = $"{Url}/slots?doctorProfileId=31&serviceId=51&date=2026-11-02";
        var before = await client.GetFromJsonAsync<AppointmentSlotsResponse>(path);
        Assert.Equal("Asia/Ulaanbaatar", before!.TimeZoneId);
        Assert.Equal(13, before.Slots.Length);
        Assert.Equal(1, before.Slots[0].StartUtc.Hour); Assert.Equal(15, before.Slots[0].StartUtc.Minute);
        Assert.Equal(new DateTimeOffset(2026,11,2,4,15,0,TimeSpan.Zero), before.Slots[^1].StartUtc);
        var booking = await Book(client);
        var after = await client.GetFromJsonAsync<AppointmentSlotsResponse>(path);
        Assert.DoesNotContain(after!.Slots, s => s.StartUtc.AddMinutes(-5) < booking.OccupiedEndUtc && booking.OccupiedStartUtc < s.EndUtc.AddMinutes(10));
        await AssertStatus(await client.PostAsJsonAsync(Url, Request("2026-11-02T09:30:00+08:00")), HttpStatusCode.Conflict);
        await AssertStatus(await client.PostAsJsonAsync(Url, Request("2026-11-02T09:30:00+08:00", doctor:36)), HttpStatusCode.Conflict);
        using var secondPatient = await app.AsAsync(27);
        await Book(secondPatient, Request("2026-11-02T10:00:00+08:00")); // buffered boundaries touch
        await Book(client, Request("2026-11-02T09:45:00+08:00", doctor:36)); // patient treatment boundary touches
        Assert.Equal(3, await Count(app));
        foreach (var date in new[] { "no-date", "2026-02-30", "2026-10-31", "2026-12-02" })
            await AssertStatus(await client.GetAsync($"{Url}/slots?doctorProfileId=31&serviceId=51&date={date}"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotent_replay_survives_changed_configuration_and_payload_mismatch_conflicts()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var request = Request(); var first = await Book(client, request);
        using (var exactReplay = await client.PostAsJsonAsync(Url, request))
        {
            Assert.Equal(HttpStatusCode.OK, exactReplay.StatusCode);
            Assert.Equal(first, await exactReplay.Content.ReadFromJsonAsync<AppointmentResponse>());
        }
        Assert.Equal(1, await Count(app));
        await Mutate(app, async db => { (await db.DoctorServices.SingleAsync(s => s.DoctorProfileId == 31)).DurationMinutes = 45; await db.SaveChangesAsync(); });
        using var replay = await client.PostAsJsonAsync(Url, request with { StartUtc = "2026-11-02T01:15:00Z" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<AppointmentResponse>());
        foreach (var different in new[] {request with {StartUtc = "2026-11-02T10:00:00+08:00"}, request with {DoctorProfileId = 36}, request with {ServiceId = 52}})
        {
            await AssertStatus(await client.PostAsJsonAsync(Url, different), HttpStatusCode.Conflict);
            Assert.Equal(1, await Count(app));
        }
        Assert.Equal(1, await Count(app));
    }

    [Fact]
    public async Task Another_patient_cannot_book_occupied_doctor_interval_with_a_new_request_key()
    {
        using var app = await App(); using var owner = await app.AsAsync(11);
        using var other = await app.AsAsync(27);
        var request = Request(); var first = await Book(owner, request);
        await AssertStatus(await other.PostAsJsonAsync(Url, request with { RequestId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        await Mutate(app, async db =>
        {
            var stored = await db.Appointments.AsNoTracking().SingleAsync();
            Assert.Equal(first.Id, stored.Id);
            Assert.Equal(41, stored.PatientId);
            Assert.Equal(request.RequestId, stored.RequestId);
            Assert.Equal(first.StartUtc, stored.StartUtc);
            Assert.Equal(first.OccupiedEndUtc, stored.OccupiedEndUtc);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_independent_connections_confirm_at_most_one_booking(bool duplicate)
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(duplicate ? 11 : 27);
        var request = Request();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Send(HttpClient client, CreateAppointmentRequest payload)
        { await gate.Task; return await client.PostAsJsonAsync(Url, payload); }
        var first = Task.Run(() => Send(a, request));
        var second = Task.Run(() => Send(b, duplicate ? request : Request()));
        gate.SetResult(); var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.All(responses, r => Assert.True(r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict || (duplicate && r.StatusCode == HttpStatusCode.OK)));
            Assert.Equal(1, await Count(app));
            using var retry = await a.PostAsJsonAsync(Url, request);
            Assert.Equal(duplicate || responses[0].StatusCode == HttpStatusCode.Created ? HttpStatusCode.OK : HttpStatusCode.Conflict, retry.StatusCode);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("sqlite")] [InlineData("23505")] [InlineData("40001")] [InlineData("40P01")]
    public async Task Failure_at_commit_rolls_back_insert_and_returns_safe_conflict(string state)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        if (state != "sqlite") app.Failure.Error = new DbUpdateException("synthetic-private-wrapper",
            new PostgresException("synthetic-private-provider", "ERROR", "ERROR", state));
        app.Failure.Enabled = true;
        using var failed = await client.PostAsJsonAsync(Url, Request());
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.DoesNotContain("synthetic-private", await failed.Content.ReadAsStringAsync());
        app.Failure.Enabled = false;
        Assert.Equal(0, await Count(app));
        await Book(client);
    }

    [Fact]
    public async Task Only_owner_can_read_bookings_and_body_cannot_supply_tenant_or_patient_authority()
    {
        using var app = await App(); using var owner = await app.AsAsync(11); using var other = await app.AsAsync(27);
        using var foreign = await app.AsAsync(12,202);
        using var created = await owner.PostAsJsonAsync(Url, new { doctorProfileId=31, serviceId=51,
            startUtc="2026-11-02T09:15:00+08:00", requestId=Guid.NewGuid(), clinicId=202, patientId=42, durationMinutes=1, status="Cancelled" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        Assert.Equal("Confirmed", row.Status);
        foreach (var client in new[] { other, foreign })
        {
            await AssertStatus(await client.GetAsync($"{Url}/{row.Id}"), HttpStatusCode.NotFound);
            Assert.Empty((await client.GetFromJsonAsync<AppointmentPage>($"{Url}/me"))!.Items);
        }
        await AssertStatus(await owner.PostAsJsonAsync(Url, Request(request:Guid.Empty)), HttpStatusCode.BadRequest);
        await AssertStatus(await owner.PostAsJsonAsync(Url, Request(doctor:0)), HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task Current_time_cutoff_and_inclusive_thirtieth_local_date_are_enforced()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        app.Time.Now = new(2026,11,2,1,30,0,TimeSpan.Zero);
        var slots = await client.GetFromJsonAsync<AppointmentSlotsResponse>($"{Url}/slots?doctorProfileId=31&serviceId=51&date=2026-11-02");
        Assert.NotEmpty(slots!.Slots);
        Assert.All(slots.Slots, s => Assert.True(s.StartUtc > app.Time.Now));
        await AssertStatus(await client.PostAsJsonAsync(Url, Request()), HttpStatusCode.Conflict);
        await AssertStatus(await client.PostAsJsonAsync(Url, Request("2026-11-02T09:30:00+08:00")), HttpStatusCode.Conflict);
        app.Time.Now = new(2026,11,1,0,0,0,TimeSpan.Zero);
        await Mutate(app, async db =>
        {
            db.DoctorWorkingHours.Add(new DoctorWorkingHours {ClinicId=101, DoctorProfileId=31, DayOfWeek=2, StartMinute=540, EndMinute=780});
            await db.SaveChangesAsync();
        });
        await Book(client, Request("2026-12-01T09:15:00+08:00"));
        await AssertStatus(await client.PostAsJsonAsync(Url, Request("2026-12-02T09:15:00+08:00")), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Verified_Hovd_zone_drives_slots_and_booking_instead_of_server_timezone()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Mutate(app, async db =>
        {
            var clinic = (await db.Clinics.FindAsync(101))!;
            clinic.ProvinceOrCapitalCode="84"; clinic.SoumOrDistrictCode="8401"; clinic.TimeZoneId="Asia/Hovd";
            await db.SaveChangesAsync();
        });
        var slots = await client.GetFromJsonAsync<AppointmentSlotsResponse>($"{Url}/slots?doctorProfileId=31&serviceId=51&date=2026-11-02");
        Assert.Equal("Asia/Hovd", slots!.TimeZoneId);
        Assert.Equal(new DateTimeOffset(2026,11,2,2,15,0,TimeSpan.Zero), slots.Slots[0].StartUtc);
        var row = await Book(client, Request("2026-11-02T09:15:00+07:00"));
        Assert.Equal(slots.Slots[0].StartUtc, row.StartUtc);
        Assert.Equal("Asia/Hovd", row.ClinicTimeZoneId);
    }

}
