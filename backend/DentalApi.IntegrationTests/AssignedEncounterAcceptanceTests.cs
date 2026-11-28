using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed partial class StructuredProcedureDraftTests
{
    private const string AssignedEncounterRoute = "/api/clinical/appointments/assigned";
    private const string AssignedEncounterQuery = "?from=2026-11-26&until=2026-11-28";

    [Fact]
    public async Task Assigned_encounter_list_releases_only_bounded_audited_booked_encounters()
    {
        using var app = await CreateFixture(httpEnabled: true);
        await SeedPatientHistory(app);
        using var doctor = await app.AsAsync(13);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var patient = await app.AsAsync(11);
        using var anonymous = app.Client();
        using var off = await doctor.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        configuration["Features:AssignedEncounterAcceptance"] = "true";
        configuration["Features:PerformedProcedureAcceptance"] = "false";
        using var dependencyOff = await doctor.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        Assert.Equal(HttpStatusCode.NotFound, dependencyOff.StatusCode);
        configuration["Features:PerformedProcedureAcceptance"] = "true";
        using var unauthorized = await anonymous.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        using var forbidden = await patient.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        foreach (var query in new[] {
            "?from=2026-01-01&until=2026-11-28", "?from=2026-11-28&until=2026-11-28", "?from=2026-11-26",
            AssignedEncounterQuery + "&pageSize=21", AssignedEncounterQuery + "&page=101" })
        {
            using var invalid = await doctor.GetAsync(AssignedEncounterRoute + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DoctorServices.Add(new DoctorService { ClinicId = 101, DoctorProfileId = 31, ServiceId = 52, DurationMinutes = 30, IsActive = false });
        await db.SaveChangesAsync();
        var inactiveAssignment = Encounter(307, FixedClock.Utc.AddHours(-6));
        inactiveAssignment.ServiceId = 52;
        db.Appointments.Add(inactiveAssignment);
        await db.SaveChangesAsync();
        await CheckPage(foreign, 1, []);
        await CheckPage(doctor, 1, [301, 304]);
        await CheckPage(doctor, 2, [305]);
        await CheckPage(doctor, 3, []);

        var audit = await db.Set<ClinicalAuditEvent>().OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(new[] { 301, 304, 305 }, audit.Select(row => row.AppointmentId));
        Assert.All(audit, row => Assert.Equal((101, 13, ClinicalAuditAction.HistoryRead), (row.ClinicId, row.ActorUserId, row.Action)));
        Assert.Equal(audit[0].CorrelationId, audit[1].CorrelationId);
        Assert.NotEqual(audit[1].CorrelationId, audit[2].CorrelationId);

        async Task CheckPage(HttpClient client, int page, int[] expected)
        {
            using var response = await client.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery + $"&page={page}&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, json.EnumerateObject().Count());
            Assert.Equal(page, json.GetProperty("page").GetInt32());
            Assert.Equal(2, json.GetProperty("pageSize").GetInt32());
            var items = json.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(expected, items.Select(row => row.GetProperty("appointmentId").GetInt32()));
            foreach (var row in items)
            {
                Assert.Equal(5, row.EnumerateObject().Count());
                Assert.Equal(51, row.GetProperty("serviceId").GetInt32());
                Assert.Equal("Synthetic booked service", row.GetProperty("serviceLabel").GetString());
                var start = row.GetProperty("startUtc").GetDateTimeOffset();
                var end = row.GetProperty("endUtc").GetDateTimeOffset();
                Assert.Equal(TimeSpan.Zero, start.Offset);
                Assert.Equal(TimeSpan.Zero, end.Offset);
                Assert.True(end > start);
            }
        }
    }

    [Fact]
    public async Task Assigned_encounter_list_blocks_non_testing_or_persistent_stores()
    {
        using var app = await CreateFixture(httpEnabled: true);
        app.Services.GetRequiredService<IConfiguration>()["Features:AssignedEncounterAcceptance"] = "true";
        using var doctor = await app.AsAsync(13);
        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        try
        {
            environment.EnvironmentName = "Production";
            using var denied = await doctor.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally { environment.EnvironmentName = "Testing"; }
        await AssertNoProcedureWrites(app);

        using var persistentShape = await CreateFixture(httpEnabled: true, fakePersistentMetadata: true);
        persistentShape.Services.GetRequiredService<IConfiguration>()["Features:AssignedEncounterAcceptance"] = "true";
        using var clinician = await persistentShape.AsAsync(13);
        using var blocked = await clinician.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
        Assert.Equal(0, persistentShape.OpenGuard.PersistentAttempts);
        await AssertNoProcedureWrites(persistentShape);
    }

    [Fact]
    public async Task Assigned_encounter_list_releases_nothing_if_any_returned_row_cannot_be_audited()
    {
        using var app = await CreateFixture(httpEnabled: true);
        await SeedPatientHistory(app);
        app.Services.GetRequiredService<IConfiguration>()["Features:AssignedEncounterAcceptance"] = "true";
        using var doctor = await app.AsAsync(13);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticAssignmentAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents"
            FOR EACH ROW WHEN NEW."AppointmentId" = 304 BEGIN
                SELECT RAISE(ABORT, 'SyntheticAssignmentAuditFailure');
            END;
            """);

        using var response = await doctor.GetAsync(AssignedEncounterRoute + AssignedEncounterQuery);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SyntheticAssignmentAuditFailure", body);
        Assert.DoesNotContain("items", body);
        await AssertNoProcedureWrites(app);
    }
}
