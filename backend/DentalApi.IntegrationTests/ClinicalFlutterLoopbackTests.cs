using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    [ClinicalFlutterFact]
    [Trait("Category", "ClinicalFlutterLoopback")]
    public async Task Flutter_clinician_confirms_and_reads_treatment_and_patient_history_over_loopback_TLS()
    {
        Assert.Null(Environment.GetEnvironmentVariable("NEXORA_PG_TEST_CONNECTION"));
        Assert.Null(Environment.GetEnvironmentVariable("PGPASSWORD"));
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "mobile", "pubspec.yaml")))
            root = root.Parent;
        Assert.NotNull(root);

        // Ephemeral test trust anchor and private key, never installed or exported to disk.
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));

        await using var host = await CreateFixture(httpEnabled: true, configure: factory =>
        {
            factory.UseLoopbackHost = true;
            factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listen => listen.UseHttps(certificate)));
        });
        host.Services.GetRequiredService<IConfiguration>()["Features:PatientHistoryAcceptance"] = "true";
        using var addressClient = host.CreateDefaultClient();
        var origin = Assert.IsType<Uri>(addressClient.BaseAddress);
        Assert.Equal("https", origin.Scheme);
        Assert.Equal("127.0.0.1", origin.Host);

        var options = new ProcessStartInfo(Environment.GetEnvironmentVariable("SUVDDENT_FLUTTER_TOOL")!)
        {
            WorkingDirectory = Path.Combine(root.FullName, "mobile"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "test", "--no-pub", "--reporter", "expanded", "tool/clinical_https_acceptance_test.dart" })
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
            throw new InvalidOperationException("Flutter loopback acceptance timed out.");
        }
        // Do not replay process logs that could contain credential or clinical values.
        _ = await stdout;
        _ = await stderr;
        Assert.True(process.ExitCode == 0, $"Flutter loopback acceptance failed (exit {process.ExitCode}).");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var signed = Assert.Single(await db.Set<PerformedProcedure>().AsNoTracking().ToListAsync());
        Assert.Equal(301, signed.AppointmentId);
        Assert.Equal(13, signed.DoctorUserId);
        var events = await db.Set<ClinicalAuditEvent>().AsNoTracking().ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.Single(events, row => row.Action == ClinicalAuditAction.TreatmentRecorded);
        Assert.Equal(2, events.Count(row => row.Action == ClinicalAuditAction.HistoryRead));
        Assert.All(events, row => Assert.Equal((101, 301, 13), (row.ClinicId, row.AppointmentId, row.ActorUserId)));
    }

    private sealed class ClinicalFlutterFactAttribute : FactAttribute
    {
        public ClinicalFlutterFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUVDDENT_FLUTTER_TOOL")))
                Skip = "Set SUVDDENT_FLUTTER_TOOL to run the isolated Flutter + Kestrel acceptance test.";
        }
    }
}
