using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Core;
using AzureDpsFramework;
using AzureDpsFramework.Adr;
using Microsoft.Azure.Iot.Device.Authentication;
using Microsoft.Azure.Iot.Device.Models;
using Microsoft.Azure.Iot.Device.Mqtt;
using Xunit;

namespace SkittleSorter.Tests;

public sealed class UnifiedSdkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public UnifiedSdkTests() => Directory.CreateDirectory(directory);

    private DpsConfiguration Configuration() => new()
    {
        IdScope = "test-scope", RegistrationId = "sorter",
        DeviceKeyBase64 = Convert.ToBase64String(new byte[32]),
        CsrFilePath = Path.Combine(directory, "device.csr"),
        CsrKeyFilePath = Path.Combine(directory, "device.key"),
        IssuedCertFilePath = Path.Combine(directory, "issued.pem")
    };

    [Theory]
    [InlineData("SymmetricKey", false)]
    [InlineData("X509", false)]
    [InlineData("SymmetricKey", true)]
    public async Task CertificateProfilesPersistMatchingIssuedIdentityAndReuseCsrKey(string attestation, bool encodedPem)
    {
        var config = Configuration();
        config.AttestationMethod = attestation;
        if (attestation == "X509")
        {
            var bootstrap = CertificateManager.GenerateSelfSignedCertificate("sorter");
            config.AttestationCertPath = Path.Combine(directory, "bootstrap.pem");
            config.AttestationKeyPath = Path.Combine(directory, "bootstrap.key");
            CertificateManager.SaveText(config.AttestationCertPath, bootstrap.certificatePem);
            CertificateManager.SaveText(config.AttestationKeyPath, bootstrap.privateKeyPem);
        }
        string originalKey;
        using (var session = UnifiedDeviceSession.Create(config))
        {
            var csr = Assert.IsType<Microsoft.Azure.Iot.Device.Provisioning.Models.ProvisioningCertificateSigningRequest>(
                session.Provisioning.CertificateSigningRequest);
            Assert.IsType<RequiredCertificateAuthenticationProvider>(session.Authentication);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await session.Authentication.GetPrimaryOperationalAuthenticationCredentialsAsync("hub", "sorter", null));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await session.Authentication.GetSecondaryOperationalAuthenticationCredentialsAsync("hub", "sorter", []));
            var request = new CertificateRequest("CN=sorter", (RSA)csr.PrivateKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var credentials = await session.Authentication.GetPrimaryOperationalAuthenticationCredentialsAsync(
                "hub", "sorter", [Convert.ToBase64String(encodedPem
                    ? System.Text.Encoding.UTF8.GetBytes(certificate.ExportCertificatePem()) : certificate.RawData)]);
            Assert.NotNull(credentials.ClientCertificate);
            Assert.True(credentials.ClientCertificate.HasPrivateKey);
            using var saved = CertificateManager.LoadX509WithPrivateKey(config.IssuedCertFilePath, config.CsrKeyFilePath);
            Assert.Equal(certificate.Thumbprint, saved.Thumbprint);
            originalKey = File.ReadAllText(config.CsrKeyFilePath);
        }
        File.WriteAllText(config.IssuedCertFilePath + ".connection.json", JsonSerializer.Serialize(new
        {
            config.IdScope, config.RegistrationId, DeviceId = "sorter", Hub = "hub.azure-devices.net",
            Profile = IotHubConnectionType.Mqttv3
        }));
        using var restarted = UnifiedDeviceSession.Create(config);
        Assert.Equal(originalKey, File.ReadAllText(config.CsrKeyFilePath));
        Assert.NotNull(restarted.Provisioning.CertificateSigningRequest);
        var restored = Assert.IsType<ConnectionContext>(restarted.Connection.GetCurrentConnectionContext());
        Assert.Equal("hub.azure-devices.net", restored.IotHubHostName);
        Assert.Single(restored.IssuedClientCertificates!);
        config.IdScope = "another-scope";
        Assert.Throws<InvalidDataException>(() => UnifiedDeviceSession.Create(config));
    }

    [Fact]
    public void PartialOrMismatchedCredentialsAreNotOverwritten()
    {
        var config = Configuration();
        var generated = CertificateManager.GenerateCsr("sorter");
        CertificateManager.SaveText(config.CsrKeyFilePath, generated.keyPem);
        Assert.Throws<InvalidOperationException>(() => UnifiedDeviceSession.Create(config));
        Assert.Equal(generated.keyPem, File.ReadAllText(config.CsrKeyFilePath));
        CertificateManager.SaveText(config.CsrFilePath, CertificateManager.GenerateCsr("sorter").csrPem);
        Assert.Throws<InvalidOperationException>(() => UnifiedDeviceSession.Create(config));
        CertificateManager.SaveText(config.CsrFilePath, CertificateManager.GenerateCsr("another-device").csrPem);
        Assert.Throws<InvalidOperationException>(() => UnifiedDeviceSession.Create(config));
        Assert.Equal(generated.keyPem, File.ReadAllText(config.CsrKeyFilePath));
    }

    [Fact]
    public void ExistingEcdsaCsrIsLoadedWithoutReplacingItsKey()
    {
        var config = Configuration();
        config.AutoGenerateCsr = false;
        var generated = CertificateManager.GenerateCsr("sorter", "ECDSA");
        CertificateManager.SaveText(config.CsrFilePath, generated.csrPem);
        CertificateManager.SaveText(config.CsrKeyFilePath, generated.keyPem);
        using var session = UnifiedDeviceSession.Create(config);
        Assert.NotNull(session.Provisioning.CertificateSigningRequest);
        Assert.IsAssignableFrom<ECDsa>(session.Provisioning.CertificateSigningRequest.PrivateKey);
        Assert.Equal(generated.keyPem, File.ReadAllText(config.CsrKeyFilePath));
    }

    [Fact]
    public void EnrollmentGroupDerivationUsesExactRegistrationAndDeviceKeyTakesPrecedence()
    {
        var config = Configuration();
        config.DeviceKeyBase64 = null;
        config.RegistrationId = "Mixed-Case";
        config.EnrollmentGroupKeyBase64 = Convert.ToBase64String(new byte[32]);
        var expected = HMACSHA256.HashData(new byte[32], System.Text.Encoding.UTF8.GetBytes("Mixed-Case"));
        Assert.Equal(Convert.ToBase64String(expected), config.GetDeviceKeyOrDerive());
        config.DeviceKeyBase64 = "device-key";
        Assert.Equal("device-key", config.GetDeviceKeyOrDerive());
    }

    [Fact]
    public async Task IssuedCertificateMustMatchPersistedKeyBeforeReplacingIdentity()
    {
        var config = Configuration();
        using var session = UnifiedDeviceSession.Create(config);
        var unrelated = CertificateManager.GenerateSelfSignedCertificate("sorter");
        using var certificate = X509Certificate2.CreateFromPem(unrelated.certificatePem);
        await Assert.ThrowsAsync<CryptographicException>(async () =>
            await session.Authentication.GetPrimaryOperationalAuthenticationCredentialsAsync("hub", "sorter",
                [Convert.ToBase64String(certificate.RawData)]));
        Assert.False(File.Exists(config.IssuedCertFilePath));
    }

    [Fact]
    public void CloudSelfTestCannotBeSilentlyOverriddenByOtherModes()
    {
        Assert.True(skittle_sorter.RuntimeOptions.Parse(["--cloud-self-test"]).CloudSelfTest);
        Assert.Throws<ArgumentException>(() => skittle_sorter.RuntimeOptions.Parse(["--cloud-self-test", "--self-test"]));
        Assert.Throws<ArgumentException>(() => skittle_sorter.RuntimeOptions.Parse(["--cloud-self-test", "--version"]));
        Assert.Throws<ArgumentException>(() => skittle_sorter.RuntimeOptions.Parse(["--cloud-self-test", "--ready-file", "ready.json"]));
    }

    [Fact]
    public async Task DpsLockCoversEachSessionButNeverTheHubConnection()
    {
        var path = Path.Combine(directory, "provisioning.lock");
        using var inner = new FakeMqttClient();
        using var client = new CoordinatedMqttClient(inner, "dps", path);
        await client.ConnectAsync(new MqttConnect { HostName = "dps" });
        Assert.Throws<IOException>(() => File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        await client.DisconnectAsync(new MqttDisconnect());
        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        await client.ConnectAsync(new MqttConnect { HostName = "hub" });
        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        await client.ConnectAsync(new MqttConnect { HostName = "dps" });
        await inner.LoseConnectionAsync();
        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        inner.FailConnection = true;
        await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync(new MqttConnect { HostName = "dps" }));
        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        using var held = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancel = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConnectAsync(new MqttConnect { HostName = "dps" }, cancel.Token));
    }

    [Fact]
    public async Task AdrHelpersUseCurrentRegistryDeviceApiAndWritableMetadata()
    {
        using var handler = new RecordingArmHandler();
        using var http = new HttpClient(handler);
        using var client = new AdrDeviceRegistryClient(new TestCredential(), http);
        await client.ListDevicesAsync("subscription", "group", "namespace");
        Assert.EndsWith("/registryDevices?api-version=2026-11-02-preview", handler.Url);
        await client.UpdateDeviceAsync("subscription", "group", "namespace", "sorter",
            new Dictionary<string, object> { ["model"] = "Sorter" }, false,
            new Dictionary<string, string> { ["deviceOwner"] = "Operations" }, "1.1.0");
        Assert.EndsWith("/registryDevices/sorter?api-version=2026-11-02-preview", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        var properties = body.RootElement.GetProperty("properties");
        Assert.Equal("Disabled", properties.GetProperty("enablementState").GetString());
        Assert.Equal("1.1.0", properties.GetProperty("softwareRevision").GetString());
        Assert.Equal("Sorter", properties.GetProperty("model").GetString());
        Assert.False(properties.TryGetProperty("attributes", out _));
        Assert.Equal("Operations", body.RootElement.GetProperty("tags").GetProperty("deviceOwner").GetString());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.UpdateDeviceAsync("subscription", "group", "namespace", "sorter",
                new Dictionary<string, object> { ["deviceOwner"] = "unsupported attribute" }));
    }

    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RecordingArmHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Get ? """{"value":[]}""" : """{"name":"sorter","properties":{}}""")
            };
        }
    }

    private sealed class FakeMqttClient : IMqttClient
    {
        public bool FailConnection { get; set; }
        public event Func<MqttPublishReceivedEventArgs, Task> PublishReceivedAsync { add { } remove { } }
        public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync { add { } remove { } }
        public event Func<MqttConnect, CancellationToken, Task<MqttConnect>> ConnectingAsync { add { } remove { } }
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public Task LoseConnectionAsync() => DisconnectedAsync?.Invoke(new MqttClientDisconnectedEventArgs()) ?? Task.CompletedTask;
        public Task<MqttConnectAck> ConnectAsync(MqttConnect connect, CancellationToken cancellationToken = default) =>
            FailConnection ? Task.FromException<MqttConnectAck>(new IOException("Connection failed.")) : Task.FromResult(new MqttConnectAck());
        public Task DisconnectAsync(MqttDisconnect disconnect, CancellationToken cancellationToken = default) => LoseConnectionAsync();
        public Task<MqttPublishAck> PublishAsync(MqttPublish publish, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MqttSubscribeAck> SubscribeAsync(MqttSubscribe subscribe, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MqttUnsubscribeAck> UnsubscribeAsync(MqttUnsubscribe unsubscribe, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool IsConnected() => false;
        public void Dispose() { }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
