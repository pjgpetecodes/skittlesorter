using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Iot.Device.Authentication;
using Microsoft.Azure.Iot.Device.Models;
using Microsoft.Azure.Iot.Device.MqttNetAdapter;
using Microsoft.Azure.Iot.Device.MQTTnetAdapter;
using Microsoft.Azure.Iot.Device.Provisioning.Models;
using Microsoft.Azure.Iot.Device.Unified.Connection;
using Microsoft.Azure.Iot.Device.Unified.Models;
using Microsoft.Azure.Iot.Device.Unified.Telemetry;

namespace AzureDpsFramework;

public sealed class UnifiedDeviceSession : IDisposable
{
    private readonly List<IDisposable> credentials = new();
    private readonly DpsConfiguration configuration;
    private readonly CoordinatedMqttClient mqtt;
    private readonly TaskCompletionSource<InvalidOperationException> credentialFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AuthenticationProvider Authentication { get; private set; } = null!;
    public ProvisioningSettings Provisioning { get; private set; } = null!;
    public ConnectionClient Connection { get; private set; } = null!;
    public TelemetryClient Telemetry { get; private set; } = null!;
    public ConnectionContext? Context { get; private set; }

    private UnifiedDeviceSession(DpsConfiguration configuration)
    {
        this.configuration = configuration;
        mqtt = new CoordinatedMqttClient(new MqttNetClient(new MqttNetClientOptions
        {
            EnableMqttLogs = configuration.EnableDebugLogging
        }), configuration.ProvisioningHost, configuration.ProvisioningLockPath);
    }

    public static UnifiedDeviceSession Create(DpsConfiguration configuration)
    {
        configuration.Validate();
        var session = new UnifiedDeviceSession(configuration);
        try
        {
            session.Initialize();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        Provisioning = new ProvisioningSettings(configuration.IdScope)
        {
            RegistrationId = configuration.RegistrationId,
            GlobalEndpointAddress = configuration.ProvisioningHost
        };
        Func<IReadOnlyList<string>, Task<X509Certificate2>>? prepareCertificate = null;
        if (configuration.RequestOperationalCertificate)
        {
            var (key, csr) = LoadSigningRequest(configuration);
            credentials.Add(key);
            Provisioning.CertificateSigningRequest = new ProvisioningCertificateSigningRequest(key, csr);
            prepareCertificate = chain =>
            {
                try
                {
                    if (chain.Count == 0) throw new InvalidOperationException("DPS returned an empty certificate chain.");
                    var pem = NormalizeIssuedChain(chain);
                    // Validate that issuance used the persisted private key before replacing the saved identity.
                    using var leaf = X509Certificate2.CreateFromPem(pem, File.ReadAllText(configuration.CsrKeyFilePath));
                    CertificateManager.SaveIssuedCertificatePem(configuration.IssuedCertFilePath, pem);
                    var certificate = CertificateManager.LoadX509WithPrivateKey(
                        configuration.IssuedCertFilePath, configuration.CsrKeyFilePath);
                    credentials.Add(certificate);
                    return Task.FromResult(certificate);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Certificate] Cannot prepare the issued operational certificate: {ex.Message}");
                    credentialFailure.TrySetResult(new InvalidOperationException("Cannot prepare the issued operational certificate.", ex));
                    throw;
                }
            };
        }

        if (configuration.AttestationMethod == "SymmetricKey")
        {
            var key = Convert.FromBase64String(configuration.GetDeviceKeyOrDerive());
            Authentication = prepareCertificate == null
                ? new SymmetricKeyAuthenticationProvider(configuration.RegistrationId, configuration.IdScope, key, null,
                    dpsSasTokenTimeToLive: TimeSpan.FromSeconds(configuration.SasExpirySeconds))
                : new SymmetricKeyAuthenticationProvider(configuration.RegistrationId, configuration.IdScope, key,
                    null, prepareCertificate, dpsSasTokenTimeToLive: TimeSpan.FromSeconds(configuration.SasExpirySeconds));
        }
        else
        {
            var certificate = CertificateManager.LoadX509WithPrivateKey(
                configuration.AttestationCertPath!, configuration.AttestationKeyPath!);
            credentials.Add(certificate);
            var chain = new X509Certificate2Collection();
            if (!string.IsNullOrWhiteSpace(configuration.AttestationCertChainPath))
                chain.ImportFromPemFile(configuration.AttestationCertChainPath);
            foreach (var item in chain) credentials.Add(item);
            Authentication = prepareCertificate == null
                ? new X509AuthenticationProvider(certificate, null, certificateChain: chain)
                : new X509AuthenticationProvider(certificate, null, prepareCertificate, certificateChain: chain);
            if (!string.Equals(Authentication.GetRegistrationId(), configuration.RegistrationId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The X.509 bootstrap certificate CN must match RegistrationId.");
        }

        if (configuration.RequestOperationalCertificate)
            Authentication = new RequiredCertificateAuthenticationProvider(Authentication, error =>
            {
                if (credentialFailure.TrySetResult(error)) Console.Error.WriteLine($"[DPS] {error.Message}");
            });
        Connection = new ConnectionClient(new ConnectionClientOptions
        {
            MqttClient = mqtt,
            EnableMqttLogging = configuration.EnableDebugLogging,
            EnableTwin = false,
            EnableDirectMethods = false,
            EnableCloudToDeviceMessages = false
        }, LoadContext());
        Telemetry = new TelemetryClient(Connection);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connecting = Connection.ProvisionAndConnectAsync(Provisioning, Authentication, cancellationToken: connectCancellation.Token);
        if (await Task.WhenAny(connecting, credentialFailure.Task) == credentialFailure.Task)
            connectCancellation.Cancel();
        try { Context = await connecting; }
        catch (Exception ex) when (credentialFailure.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(credentialFailure.Task.Result.Message, credentialFailure.Task.Result.InnerException ?? ex);
        }
        catch (Exception)
        {
            var context = Connection.GetCurrentConnectionContext();
            Console.Error.WriteLine($"[IoT] Connection failed. Assigned Hub={context?.IotHubHostName ?? "(none)"}, profile={context?.ConnectionProfile}, issued certificate count={context?.IssuedClientCertificates?.Count ?? 0}.");
            throw;
        }
        if (configuration.RequestOperationalCertificate)
            CertificateManager.SaveText(ContextPath, JsonSerializer.Serialize(new SavedAssignment(
                configuration.IdScope, configuration.RegistrationId, Context.DeviceId, Context.IotHubHostName, Context.ConnectionProfile)));
    }

    private string ContextPath => configuration.IssuedCertFilePath + ".connection.json";
    private sealed record SavedAssignment(string IdScope, string RegistrationId, string DeviceId, string Hub, IotHubConnectionType Profile);

    private static string NormalizeIssuedChain(IReadOnlyList<string> chain)
    {
        var result = new StringBuilder();
        foreach (var value in chain)
        {
            var bytes = Convert.FromBase64String(value);
            var text = Encoding.UTF8.GetString(bytes);
            if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                var certificates = new X509Certificate2Collection();
                try
                {
                    certificates.ImportFromPem(text);
                    if (certificates.Count == 0) throw new CryptographicException("Issued PEM contains no certificates.");
                    foreach (var certificate in certificates) result.AppendLine(certificate.ExportCertificatePem());
                }
                finally { foreach (var certificate in certificates) certificate.Dispose(); }
            }
            else
            {
                using var certificate = X509CertificateLoader.LoadCertificate(bytes);
                result.AppendLine(certificate.ExportCertificatePem());
            }
        }
        return result.ToString();
    }

    private ConnectionContext? LoadContext()
    {
        if (!configuration.RequestOperationalCertificate || !File.Exists(ContextPath) || !File.Exists(configuration.IssuedCertFilePath))
            return null;
        var saved = JsonSerializer.Deserialize<SavedAssignment>(File.ReadAllText(ContextPath))
            ?? throw new InvalidDataException("Saved IoT Hub assignment is empty.");
        if (saved.IdScope != configuration.IdScope || saved.RegistrationId != configuration.RegistrationId)
            throw new InvalidDataException("Saved IoT Hub assignment does not match the selected DPS profile.");
        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPemFile(configuration.IssuedCertFilePath);
            if (chain.Count == 0) throw new InvalidDataException("Saved issued certificate chain is empty.");
            if (chain[0].NotAfter.ToUniversalTime() <= DateTime.UtcNow) return null;
            return new ConnectionContext
            {
                AuthenticationProvider = Authentication, DeviceId = saved.DeviceId, IotHubHostName = saved.Hub,
                ConnectionProfile = saved.Profile,
                IssuedClientCertificates = chain.Cast<X509Certificate2>().Select(c => Convert.ToBase64String(c.RawData)).ToArray()
            };
        }
        finally { foreach (var certificate in chain) certificate.Dispose(); }
    }

    private static (AsymmetricAlgorithm Key, string Csr) LoadSigningRequest(DpsConfiguration configuration)
    {
        var csrExists = File.Exists(configuration.CsrFilePath);
        var keyExists = File.Exists(configuration.CsrKeyFilePath);
        if (csrExists != keyExists)
            throw new InvalidOperationException("Incomplete persisted CSR/key pair; restore the matching files rather than overwriting the device key.");
        if (!csrExists)
        {
            if (!configuration.AutoGenerateCsr)
                throw new InvalidOperationException("CSR and private key files are required when AutoGenerateCsr is disabled.");
            if (File.Exists(configuration.IssuedCertFilePath))
                throw new InvalidOperationException("An issued certificate exists without its CSR/key; restore its private key before provisioning.");
            var generated = CertificateManager.GenerateCsr(configuration.RegistrationId);
            CertificateManager.SaveText(configuration.CsrKeyFilePath, generated.keyPem);
            CertificateManager.SaveText(configuration.CsrFilePath, generated.csrPem);
        }
        var pem = File.ReadAllText(configuration.CsrFilePath);
        var request = CertificateRequest.LoadSigningRequestPem(pem, HashAlgorithmName.SHA256);
        if (!request.SubjectName.RawData.SequenceEqual(new X500DistinguishedName($"CN={configuration.RegistrationId}").RawData))
            throw new InvalidOperationException("Persisted CSR subject does not match RegistrationId.");
        AsymmetricAlgorithm key = request.PublicKey.Oid.Value switch
        {
            "1.2.840.113549.1.1.1" => RSA.Create(),
            "1.2.840.10045.2.1" => ECDsa.Create(),
            _ => throw new InvalidOperationException("Only RSA and ECDSA CSR keys are supported.")
        };
        try
        {
            var keyPem = File.ReadAllText(configuration.CsrKeyFilePath);
            if (key is RSA rsa) rsa.ImportFromPem(keyPem);
            else ((ECDsa)key).ImportFromPem(keyPem);
            var publicKey = key is RSA r ? r.ExportSubjectPublicKeyInfo() : ((ECDsa)key).ExportSubjectPublicKeyInfo();
            if (!publicKey.SequenceEqual(request.PublicKey.ExportSubjectPublicKeyInfo()))
                throw new InvalidOperationException("Persisted CSR and private key do not match.");
            var fields = PemEncoding.Find(pem);
            return (key, pem[fields.Base64Data].Replace("\r", "").Replace("\n", ""));
        }
        catch { key.Dispose(); throw; }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        Connection.DisconnectAsync(cancellationToken: cancellationToken);

    public void Dispose()
    {
        Telemetry?.Dispose();
        Connection?.Dispose();
        mqtt.Dispose();
        foreach (var credential in credentials) credential.Dispose();
        credentials.Clear();
    }
}
