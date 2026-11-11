using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AzureDpsFramework;
using Microsoft.Azure.Iot.Device.Authentication;
using Xunit;

namespace SkittleSorter.Tests;

public sealed class UpdateReadinessTests
{
    [Fact]
    public void StandardProfileUsesDeviceKeyWithoutRequestingCertificate()
    {
        var config = new DpsConfiguration
        {
            IdScope = "test-scope",
            RegistrationId = "sorter",
            RequestOperationalCertificate = false,
            DeviceKeyBase64 = Convert.ToBase64String(new byte[32]),
            CsrFilePath = "",
            CsrKeyFilePath = "",
            IssuedCertFilePath = ""
        };
        using var session = DeviceProvisioningIdentity.Create(config);
        Assert.IsType<SymmetricKeyAuthenticationProvider>(session.Authentication);
        Assert.Null(session.Provisioning.CertificateSigningRequest);
    }

    [Fact]
    public void ExistingProfileStillRequiresCertificateIssuance()
    {
        var config = new DpsConfiguration();
        Assert.True(config.RequestOperationalCertificate);
        Assert.Throws<InvalidOperationException>(() => DeviceProvisioningIdentity.Create(config));
    }

    [Fact]
    public void StandardX509ProfileUsesBootstrapCertificateWithoutCsr()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=sorter", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var certPath = Path.Combine(directory, "bootstrap.pem");
            var keyPath = Path.Combine(directory, "bootstrap.key");
            File.WriteAllText(certPath, certificate.ExportCertificatePem());
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            using var session = DeviceProvisioningIdentity.Create(new DpsConfiguration
            {
                IdScope = "test-scope", RegistrationId = "sorter", AttestationMethod = "X509",
                RequestOperationalCertificate = false, AttestationCertPath = certPath, AttestationKeyPath = keyPath,
                CsrFilePath = "", CsrKeyFilePath = "", IssuedCertFilePath = ""
            });
            var x509 = Assert.IsType<X509AuthenticationProvider>(session.Authentication);
            Assert.True(x509.PrimaryClientCertificate.HasPrivateKey);
            Assert.Equal(certificate.Thumbprint, x509.PrimaryClientCertificate.Thumbprint);
            Assert.Equal("sorter", x509.GetRegistrationId());
            Assert.Null(session.Provisioning.CertificateSigningRequest);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CountsAreCaseInsensitiveAndTelemetryIsSnapshot()
    {
        var counts = new skittle_sorter.SessionCounts();
        counts.RecordCompletedDrop("Red");
        counts.RecordCompletedDrop("red");
        counts.RecordCompletedDrop("green");
        var telemetry = skittle_sorter.TelemetryService.CreateTelemetry("green", "sorter", counts);
        counts.RecordCompletedDrop("red");
        Assert.Equal(4, counts.Total);
        Assert.Equal(counts.Total, counts.PerColour.Values.Sum());
        Assert.Equal(3L, telemetry["sessionTotal"]);
        Assert.Equal(2, ((Dictionary<string, long>)telemetry["sessionCounts"])["red"]);
        Assert.Equal(skittle_sorter.ApplicationRelease.Version, telemetry["appVersion"]);
        Assert.False(skittle_sorter.TelemetryService.CreateTelemetry("red", "sorter", null).ContainsKey("sessionCounts"));
    }

    [Fact]
    public void EmptyDetectionDoesNotBecomeACompletedDrop()
    {
        var counts = new skittle_sorter.SessionCounts();
        Assert.Throws<ArgumentException>(() => counts.RecordCompletedDrop("None"));
        Assert.Equal(0, counts.Total);
        Assert.Empty(counts.PerColour);
        Assert.Equal(0, new skittle_sorter.SessionCounts().Total);
    }

    [Fact]
    public void ConfigSelectionAndSelfTestDoNotRequireDeviceHardware()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "selected.json");
            File.WriteAllText(path, """{"Hardware":{},"IoTHub":{"DpsProvisioning":{"IdScope":"selected","RegistrationId":"sorter","AttestationMethod":"SymmetricKey","DeviceKeyBase64":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","RequestOperationalCertificate":false}}}""");
            var options = skittle_sorter.RuntimeOptions.Parse(["--config", path, "--self-test"]);
            options.ValidateConfiguration();
            var config = DpsConfiguration.Load(options.ConfigurationPath);
            Assert.Equal("selected", config.IdScope);
            Assert.False(config.RequestOperationalCertificate);
            Assert.Throws<ArgumentException>(() => skittle_sorter.RuntimeOptions.Parse(["--config"]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ReadinessIdentifiesTheActualRunningVersionAndProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ready.json");
        try
        {
            skittle_sorter.ApplicationRelease.WriteReadiness(path);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(Environment.ProcessId, json.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(skittle_sorter.ApplicationRelease.Version, json.RootElement.GetProperty("version").GetString());
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
