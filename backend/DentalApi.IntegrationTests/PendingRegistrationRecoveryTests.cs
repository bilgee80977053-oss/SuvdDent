using System.Net;
using System.Net.Http.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PendingRegistrationRecoveryTests
{
    private const string Email = "pending-recovery@example.invalid";
    private const string Code = "274190";

    [Fact]
    public async Task Pending_patient_can_verify_email_with_no_user_id_then_login()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var beforeOtherClinic = await app.SnapshotBAsync();
        await SeedPendingAsync(app, Role.Patient);

        // An email address alone never activates a pending registration.
        using (var wrong = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = "000000", clinicId = 101 }))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using (var foreign = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = Code, clinicId = 202 }))
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        using (var stillPending = await client.PostAsJsonAsync("/api/Auth/login",
            new { identifier = Email, password = DentalApiFactory.Password, clinicId = 101 }))
            Assert.Equal(HttpStatusCode.Unauthorized, stillPending.StatusCode);

        using (var verified = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = Code, clinicId = 101 }))
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        using var loggedIn = await client.PostAsJsonAsync("/api/Auth/login",
            new { identifier = Email, password = DentalApiFactory.Password, clinicId = 101 });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        Assert.Equal(beforeOtherClinic, await app.SnapshotBAsync());
    }

    // Keep this case separate: each authentication test must remain inside
    // the production limit of five auth requests per IP per minute.
    [Fact]
    public async Task Completed_email_verification_code_cannot_be_replayed()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var beforeOtherClinic = await app.SnapshotBAsync();
        await SeedPendingAsync(app, Role.Patient);

        using (var verified = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = Code, clinicId = 101 }))
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        using (var replay = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = Code, clinicId = 101 }))
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var loggedIn = await client.PostAsJsonAsync("/api/Auth/login",
            new { identifier = Email, password = DentalApiFactory.Password, clinicId = 101 });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        Assert.Equal(beforeOtherClinic, await app.SnapshotBAsync());
    }

    [Fact]
    public async Task Pending_staff_cannot_be_activated_by_email_recovery()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        await SeedPendingAsync(app, Role.Doctor);

        using var response = await client.PostAsJsonAsync("/api/Auth/verify-email",
            new { email = Email, code = Code, clinicId = 101 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Users.SingleAsync(u => u.Email == Email)).IsActive);
    }

    private static async Task SeedPendingAsync(DentalApiFactory app, Role role)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            ClinicId = 101,
            Email = Email,
            FullName = "Pending synthetic user",
            Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4),
            IsActive = false,
            IsEmailVerified = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Channel = "email",
            Purpose = "registration",
            CodeHash = BCrypt.Net.BCrypt.HashPassword(Code, workFactor: 4),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();
    }
}
