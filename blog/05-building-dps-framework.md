# Building the Custom DPS Framework

[← Previous: Configuring Enrollment Groups](04-configuring-enrollment-groups.md) | [Next: Building the Device Application →](06-building-device-application.md)

---

In this post, we'll dive into the **custom Azure DPS framework** that enables access to preview API features not yet available in the official Microsoft SDK. We'll explore why this framework exists, how it works, and how to use it in your applications.

## Why a Custom Framework?

As of January 2026, the official `Microsoft.Azure.Devices.Provisioning.Client` NuGet package does **not** support:

❌ **DPS Preview API** (`2025-07-01-preview`)  
❌ **CSR-based X.509 certificate issuance**  
❌ **Certificate issuance workflow** during provisioning  
❌ **Direct MQTT protocol** for DPS communication  

To access these features, we built a **custom framework** that implements the DPS MQTT protocol directly.

## Framework Architecture

```
┌──────────────────────────────────────┐
│   ProvisioningDeviceClient           │  ← Main API (matches Microsoft SDK)
└────────────┬─────────────────────────┘
             │
             │ Uses
             ▼
┌──────────────────────────────────────┐
│   ProvisioningTransportHandler       │  ← MQTT communication layer
└────────────┬─────────────────────────┘
             │
             │ Uses
             ▼
┌──────────────────────────────────────┐
│   SecurityProvider                   │  ← Authentication methods
│   ├─ SymmetricKey                    │     (Symmetric key, X.509)
│   ├─ X509                            │
│   ├─ X509Csr (with CSR generation)   │
│   └─ X509CsrWithCert (X509 + CSR)    │
└──────────────────────────────────────┘
```

## Core Components

### 1. ProvisioningDeviceClient

The main entry point that matches the official Microsoft SDK API:

```csharp
// Create client (familiar API pattern)
var client = ProvisioningDeviceClient.Create(
    globalDeviceEndpoint: "global.azure-devices-provisioning.net",
    idScope: "0ne00XXXXXX",
    securityProvider: securityProvider,
    transport: new ProvisioningTransportHandlerMqtt()
);

// Register device (returns assignment + certificate)
DeviceRegistrationResult result = await client.RegisterAsync();

// Get assigned IoT Hub and issued certificate
string assignedHub = result.AssignedHub;
string[] certificateChain = result.IssuedCertificateChain;
```

### 2. SecurityProvider

Abstract base class for authentication methods:

```csharp
public abstract class SecurityProvider
{
    // Device identity
    public abstract string GetRegistrationID();
    
    // Generate authentication credentials
    public abstract Task<string> GetAuthenticationAsync();
    
    // Optional: CSR for certificate issuance
    public virtual byte[]? GetCertificateSigningRequest() => null;
}
```

**Implementations:**

#### SecurityProviderSymmetricKey
```csharp
public class SecurityProviderSymmetricKey : SecurityProvider
{
    private readonly string _registrationId;
    private readonly string _primaryKey;
    
    public SecurityProviderSymmetricKey(string registrationId, string primaryKey)
    {
        _registrationId = registrationId;
        _primaryKey = primaryKey;
    }
    
    public override string GetRegistrationID() => _registrationId;
    
    public override Task<string> GetAuthenticationAsync()
    {
        // Generate SAS token for DPS
        var sasToken = DpsSasTokenGenerator.GenerateSasToken(
            idScope, _registrationId, _primaryKey);
        return Task.FromResult(sasToken);
    }
}
```

#### SecurityProviderX509Csr (NEW)
```csharp
public class SecurityProviderX509Csr : SecurityProvider
{
    private readonly string _registrationId;
    private readonly RSA _rsa;
    private byte[]? _csrDer;
    
    public SecurityProviderX509Csr(string registrationId, RSA? rsa = null)
    {
        _registrationId = registrationId;
        _rsa = rsa ?? RSA.Create(2048);
    }
    
    public override string GetRegistrationID() => _registrationId;
    
    public override Task<string> GetAuthenticationAsync()
    {
        // For symmetric key attestation, this would return SAS token
        // For X.509, the MQTT transport handles TLS client cert auth
        throw new NotImplementedException("Use SecurityProviderSymmetricKey");
    }
    
    public override byte[]? GetCertificateSigningRequest()
    {
        if (_csrDer == null)
        {
            // Generate CSR once
            var request = new CertificateRequest(
                $"CN={_registrationId}",
                _rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            
            _csrDer = request.CreateSigningRequest();
        }
        
        return _csrDer;
    }
    
    public RSA GetRSA() => _rsa;  // For combining with issued cert
}
```

### 3. ProvisioningTransportHandler

Handles MQTT communication with DPS:

```csharp
public class ProvisioningTransportHandlerMqtt : ProvisioningTransportHandler
{
    private const string MqttBroker = "global.azure-devices-provisioning.net";
    private const int MqttPort = 8883;
    
    public override async Task<DeviceRegistrationResult> RegisterAsync(
        ProvisioningTransportRegisterMessage message,
        CancellationToken cancellationToken)
    {
        // 1. Build MQTT client options
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttBroker, MqttPort)
            .WithTls()  // TLS 1.2+
            .WithClientId(message.RegistrationId)
            .WithCredentials(
                username: BuildUsername(message.IdScope, message.RegistrationId),
                password: message.Authentication  // SAS token
            )
            .Build();
        
        // 2. Connect to DPS
        var mqttClient = new MqttFactory().CreateMqttClient();
        await mqttClient.ConnectAsync(options, cancellationToken);
        
        // 3. Subscribe to response topic
        await mqttClient.SubscribeAsync("$dps/registrations/res/#");
        
        // 4. Build registration payload
        var payload = new {
            registrationId = message.RegistrationId,
            csr = message.Csr != null 
                ? Convert.ToBase64String(message.Csr) 
                : null
        };
        
        // 5. Publish registration request
        string requestId = Guid.NewGuid().ToString();
        string topic = $"$dps/registrations/PUT/iotdps-register/?$rid={requestId}";
        
        await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        
        // 6. Wait for response and poll if needed
        return await WaitForRegistrationResult(mqttClient, message, cancellationToken);
    }
    
    private string BuildUsername(string idScope, string registrationId)
    {
        return $"{idScope}/registrations/{registrationId}/api-version=2025-07-01-preview&ClientVersion=1.0.0";
    }
}
```

## MQTT Protocol Details

### Connection Parameters

```
Broker:   global.azure-devices-provisioning.net:8883
Protocol: MQTT 3.1.1 over TLS 1.2+
ClientID: {registrationId}
Username: {idScope}/registrations/{registrationId}/api-version=2025-07-01-preview&ClientVersion=1.0.0
Password: {SAS-token}  (for symmetric key attestation)
```

### Topics

**Subscribe (receive responses):**
```
$dps/registrations/res/#
```

**Publish (register device):**
```
$dps/registrations/PUT/iotdps-register/?$rid={requestId}
```

**Publish (check status):**
```
$dps/registrations/GET/iotdps-get-operationstatus/?$rid={requestId}&operationId={operationId}
```

### Registration Payload

```json
{
  "registrationId": "dev001-skittlesorter",
  "csr": "MIICXTCCAUUCAQAwGDEWMBQGA1UEAxMNZGV2MDAxLXNraXR0bGVzb3J0ZXIwggEi..."
}
```

**CSR Format:** Base64-encoded DER (not PEM!)

### Response Payloads

**Initial Response (assigning):**
```json
{
  "operationId": "5.afa73db6918a13a9.cf71e45a-b81b-437d-9b4f-6b4c574be43a",
  "status": "assigning"
}
```

**Final Response (assigned):**
```json
{
  "operationId": "5.afa73db6918a13a9.cf71e45a-b81b-437d-9b4f-6b4c574be43a",
  "status": "assigned",
  "registrationState": {
    "deviceId": "dev001-skittlesorter",
    "assignedHub": "dev001-skittlesorter-hub.azure-devices.net",
    "issuedCertificateChain": [
      "MIIDXzCCAkegAwIBAgIRALq...",  // Device certificate
      "MIIDYzCCAkugAwIBAgIRANd...",  // Intermediate CA
      "MIICHzCCAaOgAwIBAgIRAMg..."   // Root CA (optional)
    ]
  }
}
```

## Using the Framework

### Example 1: Symmetric Key with CSR

```csharp
using AzureDpsFramework;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;
using System.Security.Cryptography;

// 1. Derive device key from enrollment group key
string enrollmentGroupKey = "your-enrollment-group-key";
string registrationId = "dev001-skittlesorter";
string deviceKey = DeriveDeviceKey(enrollmentGroupKey, registrationId);

// 2. Create security provider with CSR support
var rsa = RSA.Create(2048);
var securityProvider = new SecurityProviderX509CsrWithSymmetricKey(
    registrationId, 
    deviceKey,
    rsa
);

// 3. Create provisioning client
var client = ProvisioningDeviceClient.Create(
    "global.azure-devices-provisioning.net",
    "0ne00XXXXXX",
    securityProvider,
    new ProvisioningTransportHandlerMqtt()
);

// 4. Register device
DeviceRegistrationResult result = await client.RegisterAsync();

Console.WriteLine($"Assigned Hub: {result.AssignedHub}");
Console.WriteLine($"Device ID: {result.DeviceId}");

// 5. Parse issued certificate
if (result.IssuedCertificateChain != null && result.IssuedCertificateChain.Length > 0)
{
    byte[] certBytes = Convert.FromBase64String(result.IssuedCertificateChain[0]);
    var certificate = new X509Certificate2(certBytes);
    
    // 6. Combine with private key
    var certWithKey = certificate.CopyWithPrivateKey(rsa);
    
    // 7. Export to PFX for persistence
    byte[] pfx = certWithKey.Export(X509ContentType.Pfx, "password");
    File.WriteAllBytes("device-cert.pfx", pfx);
    
    Console.WriteLine($"Certificate saved: device-cert.pfx");
    Console.WriteLine($"Valid until: {certificate.NotAfter}");
}

static string DeriveDeviceKey(string enrollmentKey, string registrationId)
{
    byte[] keyBytes = Convert.FromBase64String(enrollmentKey);
    using var hmac = new HMACSHA256(keyBytes);
    byte[] deviceKeyBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(registrationId));
    return Convert.ToBase64String(deviceKeyBytes);
}
```

### Example 2: X.509 Bootstrap Certificate with CSR

```csharp
// 1. Load existing bootstrap certificate
var bootstrapCert = new X509Certificate2("bootstrap.pfx", "password");

// 2. Create security provider with bootstrap cert + CSR
var rsa = RSA.Create(2048);  // New key for operational cert
var securityProvider = new SecurityProviderX509CsrWithCert(
    registrationId,
    bootstrapCert,
    rsa
);

// 3. Create provisioning client
var client = ProvisioningDeviceClient.Create(
    "global.azure-devices-provisioning.net",
    "0ne00XXXXXX",
    securityProvider,
    new ProvisioningTransportHandlerMqtt()
);

// 4. Register (authenticates with bootstrap cert, receives new cert)
DeviceRegistrationResult result = await client.RegisterAsync();

// 5. Process new certificate (same as Example 1)
```

## SAS Token Generation

For symmetric key attestation, the framework generates SAS tokens:

```csharp
public static class DpsSasTokenGenerator
{
    public static string GenerateSasToken(
        string idScope, 
        string registrationId, 
        string primaryKey)
    {
        // Resource URI
        string resourceUri = $"{idScope}/registrations/{registrationId}";
        string urlEncodedResourceUri = Uri.EscapeDataString(resourceUri);
        
        // Expiry (1 hour from now)
        long expiryTimestamp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        
        // Message to sign
        string stringToSign = $"{urlEncodedResourceUri}\n{expiryTimestamp}";
        
        // Signature
        byte[] keyBytes = Convert.FromBase64String(primaryKey);
        using var hmac = new HMACSHA256(keyBytes);
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        string signature = Convert.ToBase64String(signatureBytes);
        string urlEncodedSignature = Uri.EscapeDataString(signature);
        
        // SAS token
        return $"SharedAccessSignature sr={urlEncodedResourceUri}&sig={urlEncodedSignature}&se={expiryTimestamp}&skn=registration";
    }
}
```

## Certificate Handling

After receiving the issued certificate, the device must:

### 1. Parse Certificate Chain

```csharp
var issuedChain = result.IssuedCertificateChain;

// [0] = Device certificate
// [1] = Intermediate CA
// [2] = Root CA (optional)

byte[] deviceCertBytes = Convert.FromBase64String(issuedChain[0]);
var deviceCert = new X509Certificate2(deviceCertBytes);
```

### 2. Combine with Private Key

```csharp
// The CSR private key must be combined with the certificate
var certWithKey = deviceCert.CopyWithPrivateKey(rsa);
```

### 3. Export to PFX

Windows requires exporting to PFX format to persist the private key association:

```csharp
// Export to PFX with password
byte[] pfxBytes = certWithKey.Export(X509ContentType.Pfx, "password");
File.WriteAllBytes("device-cert.pfx", pfxBytes);

// Reload for use
var persistedCert = new X509Certificate2("device-cert.pfx", "password");
```

### 4. Install Certificate Chain (Optional)

```csharp
// Install intermediate and root CAs to system trust store
for (int i = 1; i < issuedChain.Length; i++)
{
    byte[] caCertBytes = Convert.FromBase64String(issuedChain[i]);
    var caCert = new X509Certificate2(caCertBytes);
    
    using var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadWrite);
    store.Add(caCert);
    store.Close();
}
```

## Polling for Assignment

DPS provisioning is asynchronous. The framework polls for results:

```csharp
private async Task<DeviceRegistrationResult> WaitForRegistrationResult(
    IMqttClient mqttClient,
    ProvisioningTransportRegisterMessage message,
    CancellationToken cancellationToken)
{
    string? operationId = null;
    int pollCount = 0;
    const int maxPolls = 20;
    
    while (pollCount < maxPolls)
    {
        // Wait for MQTT message
        var response = await WaitForMqttResponseAsync(mqttClient, cancellationToken);
        
        // Parse JSON response
        var json = JsonDocument.Parse(response);
        var status = json.RootElement.GetProperty("status").GetString();
        
        if (status == "assigned")
        {
            // Success! Extract assignment details
            return ParseRegistrationResult(json);
        }
        else if (status == "assigning")
        {
            // Get operation ID for polling
            operationId = json.RootElement.GetProperty("operationId").GetString();
            
            // Wait before polling
            await Task.Delay(2000, cancellationToken);
            
            // Poll for status
            await PollOperationStatusAsync(mqttClient, message, operationId, cancellationToken);
            
            pollCount++;
        }
        else if (status == "failed")
        {
            throw new Exception($"Registration failed: {json}");
        }
    }
    
    throw new TimeoutException("Device registration polling exceeded maximum attempts");
}
```

## Error Handling

```csharp
try
{
    var result = await client.RegisterAsync();
}
catch (MqttCommunicationException ex)
{
    Console.WriteLine($"MQTT connection failed: {ex.Message}");
    // Check network, DPS endpoint, TLS version
}
catch (UnauthorizedAccessException ex)
{
    Console.WriteLine($"Authentication failed: {ex.Message}");
    // Check ID Scope, registration ID, device key, enrollment group
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Registration timed out: {ex.Message}");
    // DPS may be provisioning, retry after delay
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error: {ex.Message}");
}
```

## Logging and Diagnostics

The framework includes logging stubs matching the official SDK:

```csharp
public static class Logging
{
    public static void Associate(object parent, object child)
    {
        // Log relationship between components
        Console.WriteLine($"[Associate] {parent.GetType().Name} -> {child.GetType().Name}");
    }
    
    public static void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }
    
    public static void Error(string message, Exception? ex = null)
    {
        Console.WriteLine($"[ERROR] {message}");
        if (ex != null)
            Console.WriteLine($"  Exception: {ex}");
    }
}
```

## What We Accomplished

✅ Built a custom DPS framework for preview API access  
✅ Implemented MQTT protocol communication  
✅ Created security providers for symmetric key and X.509  
✅ Added CSR generation and certificate handling  
✅ Matched official Microsoft SDK API patterns  
✅ Enabled CSR-based certificate issuance  

## Next Steps

In the next post, we'll build the **complete device application** that uses this framework:
- Loading configuration from appsettings.json
- DPS initialization and provisioning
- IoT Hub connection with issued certificate
- Sending telemetry
- Handling device twin updates

---

[Next: Building the Device Application →](06-building-device-application.md)
