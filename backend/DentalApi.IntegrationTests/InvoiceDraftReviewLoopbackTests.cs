using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed partial class StructuredProcedureDraftTests
{
    private const string InvoiceDraftReviewRoute = "/api/appointments/301/invoice-draft-review";

    [Fact]
    public async Task Invoice_draft_review_is_test_fixture_only_and_denies_persistent_metadata()
    {
        await using (var ordinary = await CreateFixture())
        {
            using var owner = await ordinary.AsAsync(11);
            using var missing = await owner.GetAsync(InvoiceDraftReviewRoute);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        await using var metadataOnly = await CreateFixture(fakePersistentMetadata: true,
            configure: app => app.WithInvoiceDraftReview());
        using var patient = await metadataOnly.AsAsync(11);
        using var denied = await patient.GetAsync(InvoiceDraftReviewRoute);
        await AssertUnavailable(denied);
        Assert.Equal(0, metadataOnly.OpenGuard.PersistentAttempts);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Invoice_draft_review_rejects_non_testing_environment_even_when_registered(string environment)
    {
        await using var fixture = await CreateFixture(configure: app => app.WithInvoiceDraftReview());
        using var owner = await fixture.AsAsync(11);
        // Only change the active request environment marker, preserving the
        // original synthetic startup and database connection.
        var marker = fixture.Services.GetRequiredService<IWebHostEnvironment>();
        var original = marker.EnvironmentName;
        marker.EnvironmentName = environment;
        try
        {
            using var denied = await owner.GetAsync(InvoiceDraftReviewRoute);
            await AssertUnavailable(denied);
        }
        finally { marker.EnvironmentName = original; }
    }

    [Fact]
    public async Task Invoice_draft_review_is_owner_bound_and_indistinguishable_when_unavailable()
    {
        await using var fixture = await CreateFixture(configure: app => app.WithInvoiceDraftReview());
        await SeedOtherInvoiceReviewPatient(fixture.Services);
        using var anonymous = fixture.Client();
        using var noToken = await anonymous.GetAsync(InvoiceDraftReviewRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        using var clinician = await fixture.AsAsync(13);
        using var wrongRole = await clinician.GetAsync(InvoiceDraftReviewRoute);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);

        using var owner = await fixture.AsAsync(11);
        using var positive = await owner.GetAsync(InvoiceDraftReviewRoute);
        Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
        Assert.True(positive.Headers.CacheControl?.NoStore);
        var response = await positive.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "appointmentId", "breakdown" },
            response.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(301, response.GetProperty("appointmentId").GetInt32());
        var breakdown = response.GetProperty("breakdown");
        Assert.False(breakdown.GetProperty("payable").GetBoolean());
        Assert.Equal("XTS", breakdown.GetProperty("currency").GetString());
        Assert.Equal("246", breakdown.GetProperty("reviewTotalUnits").GetString());

        using var wrongPatient = await fixture.AsAsync(26);
        using var wrongClinic = await fixture.AsAsync(12, 202);
        using var otherPatient = await wrongPatient.GetAsync(InvoiceDraftReviewRoute);
        using var otherClinic = await wrongClinic.GetAsync(InvoiceDraftReviewRoute);
        using var noApproval = await owner.GetAsync("/api/appointments/302/invoice-draft-review");
        using var nonexistent = await owner.GetAsync("/api/appointments/399/invoice-draft-review");
        var reference = await AssertUnavailable(nonexistent);
        Assert.Equal(reference, await AssertUnavailable(otherPatient));
        Assert.Equal(reference, await AssertUnavailable(otherClinic));
        Assert.Equal(reference, await AssertUnavailable(noApproval));

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.Patients.SingleAsync(patient => patient.UserId == 11 && patient.ClinicId == 101);
        profile.IsActive = false;
        await db.SaveChangesAsync();
        using var inactive = await owner.GetAsync(InvoiceDraftReviewRoute);
        Assert.Equal(reference, await AssertUnavailable(inactive));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoice_draft_review_rejects_incomplete_or_mismatched_server_approval(bool wrongScope)
    {
        var pricing = new ServerApprovedInvoicePricing(new(wrongScope ? 202 : 101, 301),
            Guid.Parse("e3ce2bd8-5202-420a-b346-6790af62f9e8"), wrongScope ? Guid.NewGuid() : Guid.Empty,
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(100, 0, 0)]));
        await using var fixture = await CreateFixture(configure: app => app.WithInvoiceDraftReview(pricing));
        using var patient = await fixture.AsAsync(11);
        using var denied = await patient.GetAsync(InvoiceDraftReviewRoute);
        await AssertUnavailable(denied);
    }

    [ClinicalFlutterFact]
    [Trait("Category", "ClinicalFlutterLoopback")]
    public async Task Flutter_patient_reads_only_own_approved_nonpayable_draft_review_over_loopback_TLS()
    {
        Assert.Null(Environment.GetEnvironmentVariable("NEXORA_PG_TEST_CONNECTION"));
        Assert.Null(Environment.GetEnvironmentVariable("PGPASSWORD"));
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "mobile", "pubspec.yaml")))
            root = root.Parent;
        Assert.NotNull(root);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));

        await using var fixture = await CreateFixture(configure: app =>
        {
            app.WithInvoiceDraftReview();
            app.UseLoopbackHost = true;
            app.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        });
        await SeedOtherInvoiceReviewPatient(fixture.Services);
        using var addressClient = fixture.CreateDefaultClient();
        var origin = Assert.IsType<Uri>(addressClient.BaseAddress);
        Assert.Equal(("https", "127.0.0.1"), (origin.Scheme, origin.Host));

        var options = new ProcessStartInfo(Environment.GetEnvironmentVariable("SUVDDENT_FLUTTER_TOOL")!)
        {
            WorkingDirectory = Path.Combine(root.FullName, "mobile"),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "test", "--no-pub", "--reporter", "expanded",
                     "test_support/invoice_draft_review_https_test.dart" })
            options.ArgumentList.Add(argument);
        options.Environment["SUVDDENT_LOOPBACK_ORIGIN"] = origin.AbsoluteUri;
        options.Environment["SUVDDENT_LOOPBACK_CERT_PEM"] = certificate.ExportCertificatePem();
        options.Environment["SUVDDENT_LOOPBACK_FIXTURE_PASSWORD"] = DentalApiFactory.Password;
        using var process = Process.Start(options) ?? throw new InvalidOperationException("Flutter did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new InvalidOperationException("Flutter invoice review acceptance timed out.");
        }
        var output = await stdout;
        _ = await stderr;
        Assert.True(process.ExitCode == 0, $"Flutter invoice review acceptance failed (exit {process.ExitCode}).");
        Assert.True(output.Contains("Invoice draft HTTPS acceptance completed.", StringComparison.Ordinal),
            "Flutter did not finish the invoice review acceptance.");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.Appointments.CountAsync());
        Assert.Empty(await db.Set<TreatmentRecord>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<ClinicalAuditEvent>().AsNoTracking().ToListAsync());
    }

    private static async Task SeedOtherInvoiceReviewPatient(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            Id = 26, ClinicId = 101, Role = Role.Patient, IsActive = true, IsEmailVerified = true,
            FullName = "Synthetic unrelated patient", Email = "user26@example.invalid", Phone = "00000026",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4)
        });
        db.Patients.Add(new Patient
        {
            Id = 56, UserId = 26, ClinicId = 101, FullName = "Synthetic unrelated patient", Phone = "00000026"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> AssertUnavailable(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        Assert.Equal("message", Assert.Single(json.RootElement.EnumerateObject()).Name);
        return content;
    }
}
