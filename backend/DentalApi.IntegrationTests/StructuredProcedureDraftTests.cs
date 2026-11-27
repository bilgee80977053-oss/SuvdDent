using System.Security.Claims;
using DentalApi.Clinical;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class StructuredProcedureDraftTests
{
    [Fact]
    public async Task Uses_the_assigned_encounter_and_authoritative_label_without_prices()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);

        var validated = await service.ValidateAsync(doctor, Input());
        Assert.Equal(StructuredProcedureDraftStatus.Validated, validated.Status);
        var snapshot = Assert.IsType<StructuredProcedureSnapshot>(validated.Snapshot);
        Assert.Equal((101, 41, 301, 13, 52, "Synthetic catalog label"), (snapshot.ClinicId, snapshot.PatientId,
            snapshot.AppointmentId, snapshot.DoctorUserId, snapshot.ServiceId, snapshot.ServiceLabel));
        Assert.Equal(StructuredTreatmentScope.SingleTooth, snapshot.Target.Scope);
        Assert.Equal("85", snapshot.Target.Tooth?.Code);

        var row = (await db.Services.FindAsync(52))!;
        row.Name = "Synthetic renamed service";
        row.MinPrice = 987654321;
        await db.SaveChangesAsync();
        var after = await service.ValidateAsync(doctor, new(301, 52, StructuredTreatmentScope.Visit, null));
        Assert.Equal(StructuredProcedureDraftStatus.Validated, after.Status);
        Assert.Equal("Synthetic renamed service", after.Snapshot!.ServiceLabel);
        Assert.Null(after.Snapshot.Target.Tooth);
        Assert.Equal("Synthetic catalog label", snapshot.ServiceLabel);
        Assert.Empty(await db.Set<ClinicalAuditEvent>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<TreatmentRecord>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Does_not_release_a_draft_for_a_foreign_inactive_or_invalid_catalog_or_encounter()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);
        foreach (var unavailable in new[]
        {
            Input(61), Input(53), Input(54), Input(55), Input(56), Input(int.MaxValue), Input() with { AppointmentId = 302 },
            Input() with { AppointmentId = 303 }, Input() with { AppointmentId = int.MaxValue }
        })
        {
            var result = await service.ValidateAsync(doctor, unavailable);
            Assert.Equal(StructuredProcedureDraftStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
        }

        foreach (var actor in new[]
        {
            Principal(23, 202, Role.Doctor), Principal(17, 101, Role.Doctor), Principal(15, 101, Role.Director),
            Principal(13, 202, Role.Doctor), new ClaimsPrincipal(new ClaimsIdentity())
        })
        {
            var result = await service.ValidateAsync(actor, Input());
            Assert.Equal(StructuredProcedureDraftStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
        }
    }

    [Fact]
    public async Task Rejects_invalid_targets_and_preserves_cancellation()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);
        foreach (var invalid in new[]
        {
            Input(0), Input() with { AppointmentId = 0 }, Input() with { ToothCode = "86" },
            Input() with { Scope = StructuredTreatmentScope.Visit },
            Input() with { Scope = StructuredTreatmentScope.Unspecified, ToothCode = null }
        })
        {
            var result = await service.ValidateAsync(doctor, invalid);
            Assert.Equal(StructuredProcedureDraftStatus.Invalid, result.Status);
            Assert.Null(result.Snapshot);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ValidateAsync(doctor, Input(), new CancellationToken(canceled: true)));
    }

    private static StructuredProcedureDraftInput Input(int serviceId = 52)
        => new(301, serviceId, StructuredTreatmentScope.SingleTooth, "85");

    private static ClaimsPrincipal Principal(int actor, int clinic, Role role)
        => new(new ClaimsIdentity(
            [new("userId", actor.ToString()), new("clinicId", clinic.ToString()), new(ClaimTypes.Role, role.ToString())],
            "SyntheticClinic"));

    private static async Task<DoctorApiFactory> CreateFixture()
    {
        var app = new DoctorApiFactory();
        await app.SeedAsync(); // The source factory gates migrations on synthetic, named in-memory SQLite.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Services.AddRange(
            new Service { Id = 51, ClinicId = 101, Name = "Synthetic booked service" },
            new Service { Id = 52, ClinicId = 101, Name = "Synthetic catalog label", MinPrice = 999, MaxPrice = 9999 },
            new Service { Id = 53, ClinicId = 101, Name = "Synthetic inactive", IsActive = false },
            new Service { Id = 54, ClinicId = 101, Name = " " },
            new Service { Id = 55, ClinicId = 101, Name = "Synthetic\nmalformed" },
            new Service { Id = 56, ClinicId = 101, Name = new string('a', 161) },
            new Service { Id = 61, ClinicId = 202, Name = "Synthetic foreign service" });
        await db.SaveChangesAsync();
        db.DoctorServices.Add(new DoctorService
        {
            ClinicId = 101, DoctorProfileId = 31, ServiceId = 51, DurationMinutes = 30
        });
        await db.SaveChangesAsync();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        db.Appointments.AddRange(Encounter(301, now.AddHours(-1)),
            Encounter(302, now.AddHours(-2), AppointmentStatus.Cancelled), Encounter(303, now.AddHours(2)));
        await db.SaveChangesAsync();
        return app;
    }

    private static Appointment Encounter(int id, DateTimeOffset start, AppointmentStatus status = AppointmentStatus.Confirmed) => new()
    {
        Id = id, ClinicId = 101, PatientId = 41, DoctorProfileId = 31, ServiceId = 51,
        StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start, OccupiedEndUtc = start.AddMinutes(30),
        DurationMinutesSnapshot = 30, ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid(), Status = status
    };
}
