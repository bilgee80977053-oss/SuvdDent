using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class StaffInvitationTests
{
    private static readonly InviteDoctorRequest Invite =
        new("Invited Doctor", "invited-doctor@example.invalid");

    [Fact]
    public async Task Director_invites_only_inactive_doctor_without_exposing_code()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var director = await app.AsAsync(15);
        using var response = await director.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor?clinicId=202&role=Director", Invite);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("code", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invited Doctor", body, StringComparison.OrdinalIgnoreCase);
        var mail = app.InvitationSender.Latest();
        Assert.Equal("invited-doctor@example.invalid", mail.Email);
        Assert.Matches("^[0-9]{8}$", mail.Code);
        Assert.Equal("Clinic A", mail.Clinic);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staff = await db.Users.AsNoTracking().SingleAsync(
            u => u.ClinicId == 101 && u.Email == Invite.Email);
        Assert.Equal(Role.Doctor, staff.Role);
        Assert.False(staff.IsActive);
        Assert.False(staff.IsEmailVerified);
        Assert.DoesNotContain(mail.Code, staff.PasswordHash);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
        using var login = await director.PostAsJsonAsync("/api/Auth/login", new {
            identifier = Invite.Email, password = "Guess-unused-credentials", clinicId = 101
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Invitee_accepts_once_and_sets_own_password()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var director = await app.AsAsync(15);
        using var invited = await director.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor", Invite);
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        var code = app.InvitationSender.Latest().Code;
        using var publicClient = app.Client();
        var request = new AcceptDoctorInvitationRequest(
            101, Invite.Email, code, "A-unique-secret-password-123!");
        using var accepted = await publicClient.PostAsJsonAsync(
            "/api/staff-invitations/accept", request);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var replay = await publicClient.PostAsJsonAsync(
            "/api/staff-invitations/accept", request);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var wrongClinic = await publicClient.PostAsJsonAsync(
            "/api/staff-invitations/accept",
            request with { ClinicId = 202 });
        Assert.Equal(HttpStatusCode.BadRequest, wrongClinic.StatusCode);

        using var login = await publicClient.PostAsJsonAsync("/api/Auth/login", new {
            identifier = Invite.Email, password = request.NewPassword, clinicId = 101
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("Doctor", session.GetProperty("role").GetString());

        using var eligible = await director.GetAsync("/api/admin/Doctors/eligible-users");
        Assert.Equal(HttpStatusCode.OK, eligible.StatusCode);
        var rows = (await eligible.Content.ReadFromJsonAsync<EligibleDoctorUserResponse[]>())!;
        Assert.Contains(rows, row => row.FullName == Invite.FullName);
    }

    [Theory]
    [InlineData(11, 101)]
    [InlineData(13, 101)]
    [InlineData(25, 202)]
    public async Task Only_Director_can_invite_to_own_clinic(int callerId, int callerClinic)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var client = await app.AsAsync(callerId, callerClinic);
        using var response = await client.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor", Invite);
        if (callerId == 25)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u =>
                u.ClinicId == 101 && u.Email == Invite.Email));
            Assert.True(await db.Users.AnyAsync(u =>
                u.ClinicId == 202 && u.Email == Invite.Email));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, app.InvitationSender.Count);
        }
    }

    [Fact]
    public async Task Existing_patient_is_never_promoted_by_invitation()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var director = await app.AsAsync(15);
        using var response = await director.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor",
            new InviteDoctorRequest("Synthetic user 11", "user11@example.invalid"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, app.InvitationSender.Count);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(Role.Patient, (await db.Users.FindAsync(11))!.Role);
    }

    [Fact]
    public async Task Invalid_codes_are_limited_and_cannot_activate_invited_account()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var director = await app.AsAsync(15);
        using var invited = await director.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor", Invite);
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        using var client = app.Client();
        for (var i = 0; i < 5; i++)
        {
            using var bad = await client.PostAsJsonAsync("/api/staff-invitations/accept",
                new AcceptDoctorInvitationRequest(
                    101, Invite.Email, "99999999", "Test-password-strong-123"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        using var correct = await client.PostAsJsonAsync("/api/staff-invitations/accept",
            new AcceptDoctorInvitationRequest(
                101, Invite.Email, app.InvitationSender.Latest().Code,
                "Test-password-strong-123"));
        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Users.SingleAsync(u => u.Email == Invite.Email)).IsActive);
    }

    [Fact]
    public async Task Expired_code_cannot_activate_and_staff_list_is_Director_only()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var director = await app.AsAsync(15);
        using var invited = await director.PostAsJsonAsync(
            "/api/admin/staff/invite-doctor", Invite);
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.Email == Invite.Email)
                .Select(u => u.Id).SingleAsync();
            var expiredAt = DateTime.UtcNow.AddMinutes(-1);
            await db.VerificationCodes.Where(c => c.UserId == userId &&
                c.Purpose == "doctor-invite")
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.ExpiresAt, expiredAt));
        }
        using var client = app.Client();
        using var response = await client.PostAsJsonAsync(
            "/api/staff-invitations/accept",
            new AcceptDoctorInvitationRequest(
                101, Invite.Email, app.InvitationSender.Latest().Code,
                "Test-password-strong-123"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var patient = await app.AsAsync(11);
        using var forbidden = await patient.GetAsync("/api/admin/staff/doctors");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var list = await director.GetAsync("/api/admin/staff/doctors");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var json = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codeHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Invite.Email, json);
    }
}
