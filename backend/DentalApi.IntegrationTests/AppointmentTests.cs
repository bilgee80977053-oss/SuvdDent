using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class AppointmentTests
{
    internal sealed class Fixture : IDisposable
    {
        public PlatformApiFactory App { get; } = new();
        public DateTimeOffset Start { get; } = new(DateTime.UtcNow.Date.AddDays(7 + ((int)DayOfWeek.Monday - (int)DateTime.UtcNow.DayOfWeek + 7) % 7).AddHours(1).AddMinutes(10), TimeSpan.Zero);
        public async Task Seed()
        {
            await App.SeedAsync();
            using var scope = App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var c in await db.Clinics.ToArrayAsync())
            {
                c.CountryCode = "MN"; c.ProvinceOrCapitalCode = c.Id == 101 ? "11" : "84";
                c.SoumOrDistrictCode = c.Id == 101 ? "1101" : "8401"; c.AddressLine = "Synthetic address";
                c.TimeZoneId = c.Id == 101 ? "Asia/Ulaanbaatar" : "Asia/Hovd";
                c.TimeZoneStatus = TimeZoneStatus.Verified; c.TimeZoneVerifiedClinicId = c.Id;
                c.TimeZoneVerifiedByUserId = c.Id == 101 ? 15 : 25; c.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
            }
            db.PlatformAccounts.AddRange(Enumerable.Range(1, 3).Select(i => new PlatformAccount
            { Id = i, FullName = "Patient A", Email = $"account{i}@example.invalid", IsEmailVerified = true,
                Phone = i == 1 ? "11111111" : null, IsPhoneVerified = i == 1, IsActive = true, PasswordHash = "synthetic-only" }));
            db.Services.AddRange(new Service { Id = 51, ClinicId = 101, Name = "Synthetic A", MinPrice = 100, MaxPrice = 200 },
                new Service { Id = 61, ClinicId = 202, Name = "Synthetic B", MinPrice = 150 });
            db.DoctorServices.AddRange(new DoctorService { ClinicId = 101, DoctorProfileId = 31, ServiceId = 51, DurationMinutes = 30, BufferBeforeMinutes = 5, BufferAfterMinutes = 10 },
                new DoctorService { ClinicId = 202, DoctorProfileId = 32, ServiceId = 61, DurationMinutes = 30, BufferBeforeMinutes = 5, BufferAfterMinutes = 10 });
            await db.DoctorWorkingHours.ExecuteDeleteAsync();
            db.DoctorWorkingHours.AddRange(new DoctorWorkingHours { ClinicId = 101, DoctorProfileId = 31, DayOfWeek = 1, StartMinute = 540, EndMinute = 780 },
                new DoctorWorkingHours { ClinicId = 202, DoctorProfileId = 32, DayOfWeek = 1, StartMinute = 540, EndMinute = 780 });
            await db.SaveChangesAsync();
        }
        public async Task<HttpClient> Account(int id = 1)
        {
            using var scope = App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = await db.PlatformAccounts.SingleAsync(x => x.Id == id);
            var token = scope.ServiceProvider.GetRequiredService<PlatformJwtService>().Create(a);
            var client = App.Client(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
        }
        public async Task<BookingOptionsResponse> Options(HttpClient client, int clinic = 101)
        {
            using var response = await client.GetAsync($"/api/platform/clinics/{clinic}/booking-options?doctorProfileId={(clinic == 101 ? 31 : 32)}&serviceId={(clinic == 101 ? 51 : 61)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<BookingOptionsResponse>())!;
        }
        public BookAppointmentRequest Request(BookingOptionsResponse o, DateTimeOffset? start = null, bool consent = true) =>
            new(o.DoctorProfileId, o.ServiceId, start ?? Start, o.ClinicVersion, o.DoctorVersion, consent);
        public async Task<HttpResponseMessage> Book(HttpClient client, BookAppointmentRequest request, Guid? key = null, int clinic = 101)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/platform/clinics/{clinic}/appointments");
            message.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
            message.Content = JsonContent.Create(request); return await client.SendAsync(message);
        }
        public async Task Mutate(Func<AppDbContext, Task> action)
        { using var scope = App.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        public void Dispose() => App.Dispose();
    }

    [Fact]
    public async Task Consent_is_required_and_matching_legacy_card_is_never_auto_linked()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a);
        using var denied = await f.Book(a, f.Request(o, consent: false));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Contains("PATIENT_CREATION_CONSENT_REQUIRED", await denied.Content.ReadAsStringAsync());
        await f.Mutate(async db => { Assert.Equal(2, await db.Patients.CountAsync()); Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync()); });
        using var created = await f.Book(a, f.Request(o)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await f.Mutate(async db =>
        {
            var link = await db.ClinicPatientLinks.SingleAsync(); Assert.NotEqual(41, link.PatientId);
            var patient = await db.Patients.SingleAsync(p => p.Id == link.PatientId);
            Assert.Null(patient.UserId); Assert.Null(patient.MedicalNotes); Assert.Equal("11111111", patient.Phone);
            var booking = await db.Appointments.SingleAsync(); Assert.NotNull(booking.PatientCreationConsentAtUtc);
            Assert.Equal("new-local-patient-v1", booking.PatientCreationConsentVersion);
        });
    }

    [Fact]
    public async Task One_account_books_multiple_clinics_using_separate_local_cards_and_snapshots()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account();
        var oa = await f.Options(a); var ob = await f.Options(a, 202);
        using var first = await f.Book(a, f.Request(oa));
        using var second = await f.Book(a, f.Request(ob, f.Start.AddHours(1)), clinic: 202);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var ra = (await first.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        var rb = (await second.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        Assert.Equal("Asia/Ulaanbaatar", ra.ClinicTimeZoneId); Assert.Equal("Asia/Hovd", rb.ClinicTimeZoneId);
        Assert.Equal(30, ra.DurationMinutes); Assert.Equal(f.Start.AddMinutes(30), ra.EndUtc);
        await f.Mutate(async db =>
        {
            var links = await db.ClinicPatientLinks.OrderBy(l => l.ClinicId).ToArrayAsync(); Assert.Equal(2, links.Length);
            Assert.NotEqual(links[0].PatientId, links[1].PatientId);
            await db.DoctorServices.Where(s => s.ClinicId == 101).ExecuteUpdateAsync(s => s.SetProperty(x => x.DurationMinutes, 60));
        });
        var own = (await a.GetFromJsonAsync<AppointmentResponse[]>("/api/platform/me/appointments"))!;
        Assert.Equal(2, own.Length); Assert.Equal(30, own.Single(x => x.ClinicId == 101).DurationMinutes);
    }

    [Fact]
    public async Task Idempotent_replay_returns_same_booking_and_changed_payload_conflicts()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a); var key = Guid.NewGuid();
        using var first = await f.Book(a, f.Request(o), key); using var replay = await f.Book(a, f.Request(o), key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var changed = await f.Book(a, f.Request(o, f.Start.AddHours(1)), key); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await f.Mutate(async db => { Assert.Equal(1, await db.Appointments.CountAsync()); Assert.Equal(1, await db.ClinicPatientLinks.CountAsync()); Assert.Equal(3, await db.Patients.CountAsync()); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_same_slot_or_same_key_never_creates_two_bookings(bool sameKey)
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var b = await f.Account(sameKey ? 1 : 2);
        var o = await f.Options(a); var key = Guid.NewGuid();
        var results = await Task.WhenAll(f.Book(a, f.Request(o), key), f.Book(b, f.Request(o), sameKey ? key : Guid.NewGuid()));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
            await f.Mutate(async db => { Assert.Equal(1, await db.Appointments.CountAsync()); Assert.Equal(3, await db.Patients.CountAsync()); });
        }
        finally { foreach (var r in results) r.Dispose(); }
    }

    [Fact]
    public async Task Buffers_block_overlap_but_half_open_adjacency_is_allowed()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var b = await f.Account(2); var o = await f.Options(a);
        using var first = await f.Book(a, f.Request(o)); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var overlap = await f.Book(b, f.Request(o, f.Start.AddMinutes(35))); Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using var adjacent = await f.Book(b, f.Request(o, f.Start.AddMinutes(45))); Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
    }

    [Theory]
    [InlineData(-10)] // Treatment starts at opening, but its buffer is before opening.
    [InlineData(220)] // Ends/buffer outside the working window.
    [InlineData(1440)] // Wrong local weekday.
    public async Task Working_hours_include_buffers_and_clinic_local_day(int minutes)
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a);
        using var result = await f.Book(a, f.Request(o, f.Start.AddMinutes(minutes)));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        await f.Mutate(async db => { Assert.Empty(await db.Appointments.ToArrayAsync()); Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync()); });
    }

    [Theory]
    [InlineData("clinic")]
    [InlineData("doctor")]
    [InlineData("pending")]
    [InlineData("inactive-service")]
    [InlineData("inactive-user")]
    public async Task Stale_or_unverified_or_inactive_configuration_cannot_book(string mode)
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a);
        await f.Mutate(async db =>
        {
            if (mode == "clinic") await db.Clinics.Where(c => c.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(c => c.Version, Guid.NewGuid()));
            if (mode == "doctor") await db.DoctorProfiles.Where(d => d.Id == 31).ExecuteUpdateAsync(s => s.SetProperty(d => d.Version, Guid.NewGuid()));
            if (mode == "pending") await db.Clinics.Where(c => c.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(c => c.TimeZoneStatus, TimeZoneStatus.PendingVerification).SetProperty(c => c.TimeZoneVerifiedClinicId, (int?)null).SetProperty(c => c.TimeZoneVerifiedByUserId, (int?)null).SetProperty(c => c.TimeZoneVerifiedAtUtc, (DateTime?)null));
            if (mode == "inactive-service") await db.Services.Where(s => s.Id == 51).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            if (mode == "inactive-user") await db.Users.Where(u => u.Id == 13).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        });
        using var response = await f.Book(a, f.Request(o));
        Assert.Equal(mode.StartsWith("inactive") ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, response.StatusCode);
        if (mode == "pending") Assert.Contains("CLINIC_TIMEZONE_UNVERIFIED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Account_and_patient_body_spoofing_do_not_grant_ownership_or_cross_tenant_access()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var b = await f.Account(2); var o = await f.Options(a);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/platform/clinics/101/appointments");
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        message.Content = JsonContent.Create(new { accountId = 2, patientId = 42, clinicId = 202, doctorProfileId = 31, serviceId = 51,
            startUtc = f.Start, clinicVersion = o.ClinicVersion, doctorVersion = o.DoctorVersion, consentToCreateNewPatient = true });
        using var created = await a.SendAsync(message); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = (await created.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        using var hidden = await b.GetAsync($"/api/platform/me/appointments/{booking.Id}"); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<AppointmentResponse[]>("/api/platform/me/appointments?accountId=1"))!);
        using var wrongTenant = await f.Book(a, f.Request(o) with { DoctorProfileId = 32 }, clinic: 101);
        Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
        await f.Mutate(async db => { var row = await db.Appointments.SingleAsync(); Assert.Equal(1, row.PlatformAccountId); Assert.Equal(101, row.ClinicId); Assert.NotEqual(42, row.PatientId); });
    }

    [Fact]
    public async Task Staff_sees_only_own_clinic_and_doctor_only_assigned_schedule()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a);
        using var created = await f.Book(a, f.Request(o)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var directorA = await f.App.AsAsync(15); using var directorB = await f.App.AsAsync(25, 202);
        using var doctorA = await f.App.AsAsync(13); using var otherDoctor = await f.App.AsAsync(17);
        Assert.Single((await directorA.GetFromJsonAsync<AppointmentResponse[]>("/api/appointments?clinicId=202"))!);
        Assert.Empty((await directorB.GetFromJsonAsync<AppointmentResponse[]>("/api/appointments?clinicId=101"))!);
        Assert.Single((await doctorA.GetFromJsonAsync<AppointmentResponse[]>("/api/appointments"))!);
        Assert.Empty((await otherDoctor.GetFromJsonAsync<AppointmentResponse[]>("/api/appointments"))!);
    }

    [Fact]
    public async Task Authentication_schemes_and_missing_idempotency_are_enforced()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var legacy = await f.App.AsAsync(11); using var anon = f.App.Client();
        using var noKey = await a.PostAsJsonAsync("/api/platform/clinics/101/appointments", f.Request(await f.Options(a)));
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        using var platformOnStaff = await a.GetAsync("/api/appointments"); Assert.Equal(HttpStatusCode.Unauthorized, platformOnStaff.StatusCode);
        using var legacyOnPlatform = await legacy.GetAsync("/api/platform/me/appointments"); Assert.Equal(HttpStatusCode.Unauthorized, legacyOnPlatform.StatusCode);
        using var patientOnStaff = await legacy.GetAsync("/api/appointments"); Assert.Equal(HttpStatusCode.Forbidden, patientOnStaff.StatusCode);
        using var anonymous = await anon.GetAsync("/api/platform/me/appointments"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Account_cannot_book_overlapping_times_across_clinics()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var oa = await f.Options(a); var ob = await f.Options(a, 202);
        // A booking later in its local day overlaps B's valid local opening window.
        using var first = await f.Book(a, f.Request(oa, f.Start.AddHours(1))); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await f.Book(a, f.Request(ob, f.Start.AddHours(1)), clinic: 202);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode); Assert.Contains("ACCOUNT_TIME_CONFLICT", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Database_trigger_and_composite_link_FK_protect_raw_writes()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var b = await f.Account(2); var o = await f.Options(a);
        using var first = await f.Book(a, f.Request(o)); using var second = await f.Book(b, f.Request(o, f.Start.AddHours(1)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await f.Mutate(async db =>
        {
            var all = await db.Appointments.OrderBy(x => x.Id).ToArrayAsync(); var left = all[0]; var right = all[1];
            var overlap = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Appointments SET StartUtc={left.StartUtc.UtcDateTime.Ticks},EndUtc={left.EndUtc.UtcDateTime.Ticks},OccupiedStartUtc={left.OccupiedStartUtc.UtcDateTime.Ticks},OccupiedEndUtc={left.OccupiedEndUtc.UtcDateTime.Ticks} WHERE Id={right.Id}"));
            Assert.Equal(1811, overlap.SqliteExtendedErrorCode);
            var fk = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Appointments SET PatientId=42 WHERE Id={right.Id}"));
            Assert.Equal(787, fk.SqliteExtendedErrorCode);
        });
    }
    [Fact]
    public async Task UTC_offsets_normalize_for_idempotency_and_replay_ignores_later_config_changes()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a); var key=Guid.NewGuid();
        using var first = await f.Book(a,f.Request(o,f.Start.ToOffset(TimeSpan.FromHours(8))),key);
        Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        await f.Mutate(async db => { await db.DoctorProfiles.Where(d=>d.Id==31).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Version,Guid.NewGuid())); });
        using var replay = await f.Book(a,f.Request(o,f.Start),key);
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_offset_and_past_times_cannot_create_patient_or_appointment()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); var o = await f.Options(a);
        using var msg = new HttpRequestMessage(HttpMethod.Post,"/api/platform/clinics/101/appointments");
        msg.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
        msg.Content=JsonContent.Create(new {doctorProfileId=31,serviceId=51,startUtc=f.Start.ToString("yyyy-MM-ddTHH:mm:ss"),clinicVersion=o.ClinicVersion,doctorVersion=o.DoctorVersion,consentToCreateNewPatient=true});
        using var missing=await a.SendAsync(msg);Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        using var past=await f.Book(a,f.Request(o,DateTimeOffset.UtcNow.AddDays(-1)));Assert.Equal(HttpStatusCode.BadRequest,past.StatusCode);
        await f.Mutate(async db=> {Assert.Empty(await db.Appointments.ToArrayAsync());Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync());});
    }

    [Fact]
    public async Task Insert_failure_rolls_back_new_patient_and_link_atomically()
    {
        using var f=new Fixture();await f.Seed();using var a=await f.Account();var o=await f.Options(a);
        await f.Mutate(async db=> {await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER SyntheticRejectBooking BEFORE INSERT ON Appointments BEGIN SELECT RAISE(ABORT,'synthetic-test-only'); END;");});
        using var response=await f.Book(a,f.Request(o));Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        await f.Mutate(async db=>{Assert.Equal(2,await db.Patients.CountAsync());Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync());Assert.Empty(await db.Appointments.ToArrayAsync());});
    }

    [Fact]
    public async Task Existing_link_is_reused_without_new_consent_and_unverified_phone_is_not_copied()
    {
        using var f=new Fixture();await f.Seed();using var a=await f.Account(2);var o=await f.Options(a);
        using var first=await f.Book(a,f.Request(o));Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        using var second=await f.Book(a,f.Request(o,f.Start.AddHours(1),consent:false));Assert.Equal(HttpStatusCode.Created,second.StatusCode);
        await f.Mutate(async db=>
        {
            var link=await db.ClinicPatientLinks.SingleAsync();var patient=await db.Patients.SingleAsync(p=>p.Id==link.PatientId);
            Assert.Empty(patient.Phone);Assert.Equal(3,await db.Patients.CountAsync());
            var bookings=await db.Appointments.OrderBy(x=>x.Id).ToArrayAsync();Assert.Equal(bookings[0].PatientId,bookings[1].PatientId);
            Assert.Null(bookings[1].PatientCreationConsentAtUtc);
        });
    }

}
