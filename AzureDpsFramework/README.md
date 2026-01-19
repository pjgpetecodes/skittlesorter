# Azure DPS Framework with X.509 Certificate Issuance

A C# library providing Azure Device Provisioning Service (DPS) support with X.509 certificate issuance using Certificate Signing Requests (CSR). This library implements direct MQTT protocol communication with DPS, enabling access to preview API features not yet available in the official Microsoft SDK.

## Why This Library Exists

The official `Microsoft.Azure.Devices.Provisioning.Client` NuGet package (as of January 2026) does **not** support:

- **DPS Preview API** (`2025-07-01-preview`)
- **CSR-based X.509 certificate issuance** - The new Azure Device Registry (ADR) credential policy feature that allows DPS to issue certificates dynamically
- **Certificate issuance workflow** - Submitting a CSR during registration and receiving a signed certificate

This library works around these limitations by implementing the **DPS MQTT protocol directly** using MQTTnet, following the official Azure DPS MQTT specification.

## Features

✅ **Symmetric Key Authentication for DPS** - Derive device-specific keys from enrollment group keys  
✅ **X.509 Certificate Authentication for DPS** - Use existing X.509 certificates for DPS attestation  
✅ **CSR Generation** - Auto-generate RSA or ECC certificate signing requests  
✅ **MQTT Protocol Implementation** - Direct communication with DPS MQTT broker (port 8883)  
✅ **Certificate Issuance** - Submit CSR and receive signed X.509 certificates from DPS  
✅ **Dual Attestation Support** - Both symmetric key and X.509 authentication with CSR  
✅ **SAS Token Generation** - Create DPS-compatible Shared Access Signatures  
✅ **Polling Support** - Handle async device assignment with status polling  
✅ **X.509 Certificate Loading** - Parse and load issued certificates with private keys  
✅ **Preview API Support** - Access to `2025-07-01-preview` API features

## How It Works

### Authentication Methods

The library supports **two attestation methods** for DPS provisioning:

#### Method 1: Symmetric Key Attestation + CSR (Default)

Uses enrollment group symmetric key for DPS authentication, then requests certificate via CSR.

**Phase 1: DPS Provisioning (Symmetric Key Authentication)**

```
1. Derive device key: HMACSHA256(enrollmentGroupKey, registrationId)
2. Generate SAS token using derived key
3. Connect to DPS via MQTT (port 8883 TLS)
4. Submit registration request with CSR
5. Poll for device assignment status
6. Receive assigned IoT Hub + issued certificate
```

**Phase 2: IoT Hub Connection (X.509 Authentication)**

```
1. Parse issued certificate chain (device cert + intermediates)
2. Combine device certificate with private key
3. Export to PFX and reload (fixes ephemeral key issue)
4. Connect to IoT Hub using X.509 certificate
5. Send telemetry with certificate-based auth
```

#### Method 2: X.509 Attestation + CSR (New)

Uses existing X.509 certificate for DPS authentication, then requests NEW certificate via CSR.

**Phase 1: DPS Provisioning (X.509 Authentication)**

```
1. Load existing X.509 certificate + private key (bootstrap cert)
2. Connect to DPS via MQTT with TLS client certificate authentication
3. Submit registration request with CSR for NEW certificate
4. Poll for device assignment status
5. Receive assigned IoT Hub + issued certificate
```

**Phase 2: IoT Hub Connection (X.509 Authentication)**

```
1. Parse newly issued certificate chain
2. Combine new device certificate with CSR private key
3. Export to PFX and reload
4. Connect to IoT Hub using newly issued X.509 certificate
5. Send telemetry with certificate-based auth
```

### Authentication Flow

The library implements a **two-phase authentication** approach:

#### Phase 1: DPS Provisioning (Symmetric Key Authentication)

```
1. Derive device key: HMACSHA256(enrollmentGroupKey, registrationId)
2. Generate SAS token using derived key
3. Connect to DPS via MQTT (port 8883 TLS)
4. Submit registration request with CSR
5. Poll for device assignment status
6. Receive assigned IoT Hub + issued certificate
```

#### Phase 2: IoT Hub Connection (X.509 Authentication)

```
1. Parse issued certificate chain (device cert + intermediates)
2. Combine device certificate with private key
3. Export to PFX and reload (fixes ephemeral key issue)
4. Connect to IoT Hub using X.509 certificate
5. Send telemetry with certificate-based auth
```

### MQTT Protocol Details

#### Connection

- **Broker**: `global.azure-devices-provisioning.net:8883`
- **Protocol**: MQTT over TLS
- **Username**: `{idScope}/registrations/{registrationId}/api-version=2025-07-01-preview&ClientVersion={userAgent}`
- **Password**: SAS Token (see SAS Token Format below)
- **Client ID**: `{registrationId}`

#### SAS Token Format

```
SharedAccessSignature sr={urlEncodedResourceUri}&sig={urlEncodedSignature}&se={expiryTimestamp}&skn=registration
```

**Message to Sign**:
```
{urlEncodedResourceUri}\n{expiryTimestamp}
```

**Resource URI**: `{idScope}/registrations/{registrationId}` (URL-encoded)

#### Topics

**Subscribe (responses)**:
```
$dps/registrations/res/#
```

**Publish (registration)**:
```
$dps/registrations/PUT/iotdps-register/?$rid={requestId}
```

**Publish (status polling)**:
```
$dps/registrations/GET/iotdps-get-operationstatus/?$rid={requestId}&operationId={operationId}
```

#### Registration Payload

```json
{
  "registrationId": "skittlesorter",
  "csr": "MIICXTCCAUUCAQAwGDEWMBQGA1UEAxMNc2tpdHRsZXNvcnRlcjCCASIw..."
}
```

**CSR Format**: Base64-encoded DER (extract base64 from PEM, no headers/footers)

#### Response Payload

**Initial (assigning)**:
```json
{
  "operationId": "5.afa73db6918a13a9.cf71e45a-b81b-437d-9b4f-6b4c574be43a",
  "status": "assigning"
}
```

**Final (assigned)**:
```json
{
  "operationId": "5.afa73db6918a13a9.cf71e45a-b81b-437d-9b4f-6b4c574be43a",
  "status": "assigned",
  "registrationState": {
    "deviceId": "skittlesorter",
    "assignedHub": "pjgiothub001.azure-devices.net",
    "issuedCertificateChain": [
      "MIIF7TCCBXKgAwIBAgIRAMojtpxg7WxV...",  // Device cert
      "MIIEojCCBCegAwIBAgIRANdlt7yeBF81...",  // Intermediate CA
      "MIICHzCCAaOgAwIBAgIRAMg6Fi9H5vZF..."   // Root CA
    ]
  }
}
```

## Architecture

### Core Components

#### DpsConfiguration
Loads and validates DPS settings from configuration file.

**Properties**:
- `IdScope`: DPS instance identifier
- `RegistrationId`: Device registration ID
- `AttestationMethod`: Authentication method - "SymmetricKey" or "X509"
- `ProvisioningHost`: DPS endpoint hostname
- `EnrollmentGroupKeyBase64`: Base64-encoded enrollment group primary key (for SymmetricKey)
- `AttestationCertPath`: Path to existing X.509 certificate (for X509)
- `AttestationKeyPath`: Path to private key for X.509 cert (for X509)
- `AttestationCertChainPath`: Optional certificate chain/bundle (for X509)
- `MqttPort`: MQTT broker port (default: 8883)
- `ApiVersion`: DPS API version (use `2025-07-01-preview`)
- `CsrFilePath`: Path to certificate signing request
- `CsrKeyFilePath`: Path to private key
- `IssuedCertFilePath`: Path to save issued certificate
- `AutoGenerateCsr`: Auto-generate CSR if missing
- `SasExpirySeconds`: SAS token TTL

#### DpsSasTokenGenerator
Generates DPS-compatible SAS tokens with proper symmetric key derivation.

**Key Methods**:
- `DeriveDeviceKey(registrationId, enrollmentGroupKey)` - Compute device-specific key
- `GenerateDpsSas(idScope, registrationId, deviceKey, expirySeconds)` - Create SAS token

**Implementation Details**:
```csharp
// Derive device key
var deviceKey = Convert.ToBase64String(
    new HMACSHA256(Convert.FromBase64String(enrollmentGroupKey))
        .ComputeHash(Encoding.UTF8.GetBytes(registrationId.ToLowerInvariant()))
);

// Sign message
var resourceUri = $"{idScope}/registrations/{registrationId}";
var urlEncodedUri = Uri.EscapeDataString(resourceUri);
var expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
var message = $"{urlEncodedUri}\n{expiry}";

var signature = Convert.ToBase64String(
    new HMACSHA256(Convert.FromBase64String(deviceKey))
        .ComputeHash(Encoding.UTF8.GetBytes(message))
);

var token = $"SharedAccessSignature sr={urlEncodedUri}&sig={Uri.EscapeDataString(signature)}&se={expiry}&skn=registration";
```

#### CertificateManager
Handles CSR generation, self-signed certificate generation, PEM file operations, and X.509 certificate loading.

**Key Methods**:
- `GenerateCsr(commonName, algorithm, keySize, hashAlg)` - Generate RSA or ECC CSR
- `GenerateSelfSignedCertificate(commonName, validityDays, algorithm, keySize)` - Create self-signed X.509 cert for testing
- `SaveText(path, content)` - Save PEM to file
- `LoadX509WithPrivateKey(certPath, keyPath)` - Load certificate with private key

**CSR Generation**:
```csharp
// RSA CSR
using var rsa = RSA.Create(2048);
var dn = new X500DistinguishedName($"CN={commonName}");
var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
byte[] csrDer = req.CreateSigningRequest();

// ECC CSR (alternative)
using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var req = new CertificateRequest(dn, ecdsa, HashAlgorithmName.SHA256);
byte[] csrDer = req.CreateSigningRequest();
```

**Certificate Loading** (fixes ephemeral key issue):
```csharp
// Load cert + key from PEM
var cert = X509Certificate2.CreateFromPem(certPem, keyPem);

// Export to PFX and reload to make key persistent
var pfx = cert.Export(X509ContentType.Pfx);
return new X509Certificate2(pfx);
```

#### DpsProvisioningClient
Orchestrates the complete DPS registration flow via MQTT.

**Key Method**:
- `RegisterWithCsrAsync(csrPem, cancellationToken)` - Perform full registration

**Registration Flow**:
```csharp
1. Derive device key from enrollment group key
2. Generate SAS token
3. Connect to MQTT broker with TLS
4. Subscribe to response topics ($dps/registrations/res/#)
5. Publish registration with CSR payload
6. Wait for initial response (status: "assigning")
7. Poll operationId every 2 seconds (max 20 attempts)
8. Receive final response (status: "assigned")
9. Extract deviceId, assignedHub, issuedCertificateChain
10. Disconnect and return DpsResponse
```

**Error Handling**:
- 401 Unauthorized: Invalid enrollment key or SAS token
- 400 Bad Request: Malformed CSR or payload
- Timeout: Registration takes too long (30s default)

## Usage

### Installation

Add the library to your project:

```bash
dotnet add reference ../AzureDpsFramework/AzureDpsFramework.csproj
```

Or as a NuGet package (if published):

```bash
dotnet add package AzureDpsFramework
```

### Configuration

Create `appsettings.json`:

#### Option 1: Symmetric Key Attestation (Default)

```json
{
  "IoTHub": {
    "DpsProvisioning": {
      "IdScope": "0ne01104302",
      "RegistrationId": "my-device",
      "AttestationMethod": "SymmetricKey",
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "EnrollmentGroupKeyBase64": "vI1R80rNFKEVFuwEvO2RKeYMwqCwWwQPdEAmtNrrHrWpz4Iq...",
      "AttestationCertPath": "",
      "AttestationKeyPath": "",
      "AttestationCertChainPath": "",
      "CsrFilePath": "certs/device.csr",
      "CsrKeyFilePath": "certs/device.key",
      "IssuedCertFilePath": "certs/issued.pem",
      "ApiVersion": "2025-07-01-preview",
      "AutoGenerateCsr": true,
      "MqttPort": 8883,
      "SasExpirySeconds": 3600,
      "EnableDebugLogging": false
    }
  }
}
```

#### Option 2: X.509 Attestation

```json
{
  "IoTHub": {
    "DpsProvisioning": {
      "IdScope": "0ne01104302",
      "RegistrationId": "my-device",
      "AttestationMethod": "X509",
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "EnrollmentGroupKeyBase64": "",
      "AttestationCertPath": "certs/bootstrap-cert.pem",
      "AttestationKeyPath": "certs/bootstrap-key.pem",
      "AttestationCertChainPath": "certs/bootstrap-chain.pem",
      "CsrFilePath": "certs/device.csr",
      "CsrKeyFilePath": "certs/device.key",
      "IssuedCertFilePath": "certs/issued.pem",
      "ApiVersion": "2025-07-01-preview",
      "AutoGenerateCsr": true,
      "MqttPort": 8883,
      "SasExpirySeconds": 3600,
      "EnableDebugLogging": false
    }
  }
}
```

### Code Example

```csharp
using AzureDpsFramework;
using Microsoft.Azure.Devices.Client;

// Load configuration
var config = DpsConfiguration.Load();

// Auto-generate CSR if needed
if (config.AutoGenerateCsr && !File.Exists(config.CsrFilePath))
{
    var (csrPem, keyPem) = CertificateManager.GenerateCsr(config.RegistrationId);
    CertificateManager.SaveText(config.CsrFilePath, csrPem);
    CertificateManager.SaveText(config.CsrKeyFilePath, keyPem);
}

// Read CSR
var csrPem = File.ReadAllText(config.CsrFilePath);

// Provision device with DPS
var dpsClient = new DpsProvisioningClient(config);
var response = await dpsClient.RegisterWithCsrAsync(csrPem, CancellationToken.None);

if (response.status == "assigned" && response.registrationState?.issuedCertificateChain?.Length > 0)
{
    // Save issued certificate
    var certPem = ConvertCertChainToPem(response.registrationState.issuedCertificateChain);
    CertificateManager.SaveIssuedCertificatePem(config.IssuedCertFilePath, certPem);
    
    // Load certificate with private key
    var cert = CertificateManager.LoadX509WithPrivateKey(
        config.IssuedCertFilePath,
        config.CsrKeyFilePath
    );
    
    // Connect to IoT Hub with X.509 auth
    var deviceClient = DeviceClient.Create(
        response.registrationState.assignedHub,
        new DeviceAuthenticationWithX509Certificate(response.registrationState.deviceId, cert),
        TransportType.Mqtt
    );
    
    // Send telemetry
    var message = new Message(Encoding.UTF8.GetBytes("{\"temperature\": 25.5}"));
    await deviceClient.SendEventAsync(message);
}
```

## Azure Setup Requirements

### 1. Device Provisioning Service (DPS)

Create a DPS instance in Azure Portal.

### 2. Azure Device Registry (ADR) with Credential Policy

Create an ADR namespace and credential policy for certificate issuance:

```bash
# Create ADR namespace
az iot device-registry namespace create \
  --name my-adr-namespace \
  --resource-group my-rg

# Create credential policy for ECC certificate issuance
az iot device-registry credential-policy create \
  --namespace-name my-adr-namespace \
  --credential-policy-name my-cert-policy \
  --resource-group my-rg \
  --certificate-type ECC \
  --validity-period-days 30
```

### 3. Enrollment Group with ADR Policy

Create an enrollment group in DPS with symmetric key attestation and link it to your ADR credential policy:

1. Go to DPS → Manage enrollments → Enrollment groups
2. Create new group:
   - **Attestation Type**: Symmetric Key
   - **Primary Key**: Auto-generated (copy this for config)
   - **Credential Policy**: Select your ADR policy
3. Note your **ID Scope** from DPS Overview

### 4. Link IoT Hub to DPS

1. Go to DPS → Linked IoT hubs
2. Add your IoT Hub
3. Ensure the IoT Hub accepts X.509 certificates

## Troubleshooting

### Common Issues

#### 401 Unauthorized

**Symptoms**: `[MQTT ERROR] Connection failed` with 401 status

**Causes**:
- Wrong enrollment group key in config
- Registration ID doesn't match
- SAS token signature mismatch

**Solution**:
1. Verify `EnrollmentGroupKeyBase64` matches DPS enrollment group primary key
2. Ensure `RegistrationId` is lowercase for key derivation
3. Check diagnostic logs: `[KEY DERIVATION]` and `[SAS]`

#### 400 Bad Request - Deserialization Error

**Symptoms**: `"errorCode":400012,"message":"Deserialization error"`

**Causes**:
- CSR format incorrect (should be base64 DER, not PEM)
- Wrong API version in MQTT username
- Malformed JSON payload

**Solution**:
1. Ensure using preview API: `api-version=2025-07-01-preview` in MQTT username
2. CSR must be base64-encoded DER (extract from PEM without headers)
3. Check payload format matches example above

#### TLS Authentication Error - Ephemeral Keys

**Symptoms**: `Authentication failed because the platform does not support ephemeral keys`

**Causes**:
- Certificate loaded from PEM without proper export
- Private key not persisted in Windows crypto store

**Solution**:
The library handles this automatically by exporting to PFX and reloading. Ensure you're using `CertificateManager.LoadX509WithPrivateKey()`.

#### Certificate Chain Not Received

**Symptoms**: `Certificate Chain Present: False` or `issuedCertificateChain` is null

**Causes**:
- Enrollment group not linked to ADR credential policy
- Credential policy not configured for certificate issuance
- CSR not included in registration payload

**Solution**:
1. Verify ADR credential policy is linked to enrollment group
2. Ensure `AutoGenerateCsr: true` or CSR file exists
3. Check DPS logs in Azure Portal for errors

#### Connection Timeout

**Symptoms**: `Timeout waiting for DPS response (30 seconds)`

**Causes**:
- Network connectivity issues
- DPS service unavailable
- Certificate issuance taking too long

**Solution**:
1. Check internet connectivity to `global.azure-devices-provisioning.net:8883`
2. Verify firewall allows MQTT over TLS (port 8883)
3. Check Azure service health for DPS

### Diagnostic Logging

The library outputs detailed diagnostic information:

```
[KEY DERIVATION] RegistrationId (normalized): skittlesorter
[KEY DERIVATION] Derived device key (first 20): 2oQPCWcfTc8SoySWEDaZ...

[SAS] Resource URI: 0ne01104302/registrations/skittlesorter
[SAS] URL-encoded URI (for signing): 0ne01104302%2Fregistrations%2Fskittlesorter
[SAS] Message to sign: 0ne01104302%2Fregistrations%2Fskittlesorter\n1768775857
[SAS] Signature (first 30 chars): sJs0ftAuJQlEjPMORXou1DhMf7kzyT...

[MQTT] Username: 0ne01104302/registrations/skittlesorter/api-version=2025-07-01-preview&ClientVersion=...
[MQTT] ✅ Connected successfully to DPS!

[MQTT] Status code from topic: 200
[MQTT] Response payload: {"operationId":"5.afa...","status":"assigned",...}

DPS Response Status: assigned
Device ID: skittlesorter
Assigned Hub: pjgiothub001.azure-devices.net
Certificate Chain Present: True
Certificate Chain Length: 3 certificates
```

Use these logs to diagnose authentication, MQTT, and certificate issues.

## API Reference

### DpsConfiguration

```csharp
public class DpsConfiguration
{
    public string IdScope { get; set; }
    public string RegistrationId { get; set; }
    public string ProvisioningHost { get; set; }
    public string? EnrollmentGroupKeyBase64 { get; set; }
    public string? DeviceKeyBase64 { get; set; }
    public int MqttPort { get; set; }
    public string ApiVersion { get; set; }
    public string CsrFilePath { get; set; }
    public string CsrKeyFilePath { get; set; }
    public string IssuedCertFilePath { get; set; }
    public bool AutoGenerateCsr { get; set; }
    public int SasExpirySeconds { get; set; }
    
    public static DpsConfiguration Load(string? path = null);
}
```

### DpsSasTokenGenerator

```csharp
public static class DpsSasTokenGenerator
{
    public static string DeriveDeviceKey(string registrationId, string enrollmentGroupKeyBase64);
    public static string GenerateDpsSas(string idScope, string registrationId, 
        string deviceKeyBase64, int expirySeconds);
}
```

### CertificateManager

```csharp
public static class CertificateManager
{
    public static (string csrPem, string keyPem) GenerateCsr(string commonName, 
        string algorithm = "RSA", int rsaKeySize = 2048, string hashAlg = "SHA256");
    
    public static void SaveText(string path, string content);
    public static void SaveIssuedCertificatePem(string path, string pemChain);
    
    public static X509Certificate2 LoadX509WithPrivateKey(string certPemPath, string keyPemPath);
}
```

### DpsProvisioningClient

```csharp
public class DpsProvisioningClient
{
    public DpsProvisioningClient(DpsConfiguration config);
    
    public Task<DpsResponse> RegisterWithCsrAsync(string csrPem, CancellationToken cancellationToken);
}

public class DpsResponse
{
    public string? operationId { get; set; }
    public string? status { get; set; }
    public DpsRegistrationState? registrationState { get; set; }
}

public class DpsRegistrationState
{
    public string? deviceId { get; set; }
    public string? assignedHub { get; set; }
    public string? substatus { get; set; }
    public string[]? issuedCertificateChain { get; set; }
}
```

## Dependencies

- **MQTTnet** (4.3.3.952): MQTT client library
- **Newtonsoft.Json** (13.0.3): JSON serialization
- **System.Device.Gpio** (3.2.0): Hardware abstraction (for reference)

## Security Considerations

### Enrollment Group Key Protection

The enrollment group primary key is a **master secret** that can derive keys for any device in the group. Protect it:

- ✅ Store in Azure Key Vault or secure configuration
- ✅ Use environment variables in production
- ✅ Never commit to source control
- ❌ Don't hardcode in application code

### Private Key Storage

Device private keys should be protected:

- ✅ Use restrictive file permissions (chmod 600)
- ✅ Consider hardware security modules (HSM) for production
- ✅ Rotate certificates before expiry
- ❌ Don't store in public directories

### Certificate Validation

Always validate the certificate chain:

- ✅ Verify certificate is issued by trusted CA (DPS intermediate)
- ✅ Check certificate expiry dates
- ✅ Validate subject CN matches device ID
- ✅ Use TLS for all MQTT connections

## Performance Considerations

### CSR Generation

CSR generation is CPU-intensive:

- **RSA 2048**: ~100-500ms on modern CPUs
- **ECC P-256**: ~10-50ms on modern CPUs

Consider ECC for resource-constrained devices.

### Certificate Caching

After initial provisioning, **cache the issued certificate**:

```csharp
if (File.Exists(config.IssuedCertFilePath) && !CertificateExpired())
{
    // Reuse existing certificate
    var cert = CertificateManager.LoadX509WithPrivateKey(
        config.IssuedCertFilePath,
        config.CsrKeyFilePath
    );
}
else
{
    // Re-provision
    var response = await dpsClient.RegisterWithCsrAsync(csrPem, ct);
}
```

### Polling Interval

Default polling is 2 seconds with 20 retries (40s total). Adjust based on your needs:

```csharp
// In DpsProvisioningClient.cs
for (int i = 0; i < 20; i++)  // Adjust retry count
{
    await Task.Delay(2000, ct);  // Adjust interval
    // ... poll logic
}
```

## Limitations

- **MQTT Only**: No HTTPS/AMQP support (DPS MQTT spec only)
- **Symmetric Key Attestation**: TPM and X.509 CA attestation not supported
- **Single IoT Hub**: Multi-hub allocation policies not tested
- **Preview API**: Subject to breaking changes by Azure

## Future Enhancements

- [ ] Support for TPM attestation
- [ ] HTTPS/REST API fallback
- [ ] Certificate renewal workflow
- [ ] Custom allocation policies
- [ ] Retry with exponential backoff
- [ ] Async/await throughout (remove GetAwaiter().GetResult())
- [ ] NuGet package publication

## Contributing

Contributions welcome! Please ensure:

1. All new features have XML documentation
2. Diagnostic logging for troubleshooting
3. Error handling with meaningful messages
4. Unit tests for critical paths

## License

MIT License - See LICENSE file for details

## Resources

- [Azure DPS Documentation](https://learn.microsoft.com/azure/iot-dps/)
- [DPS MQTT Protocol](https://learn.microsoft.com/azure/iot-dps/iot-dps-mqtt-support)
- [Azure Device Registry](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
- [X.509 Certificate Authentication](https://learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview)

## Support

For issues specific to this library, please open a GitHub issue with:
- DPS configuration (redact keys)
- Complete diagnostic logs
- Error messages and stack traces
- .NET version and OS

For Azure DPS service issues, contact Azure Support.
