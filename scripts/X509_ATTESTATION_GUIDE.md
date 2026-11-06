# X.509 Attestation Support for CSR-Based Provisioning

## Overview

The unified `Microsoft.Azure.Iot.Device` **2.0.0-preview4** adapter supports
**two attestation methods** for CSR-based certificate provisioning:

1. **Symmetric Key Attestation** (existing) - Uses SAS token for DPS authentication
2. **X.509 Attestation** - Uses an existing X.509 certificate for DPS authentication

Both methods submit a Certificate Signing Request (CSR) to receive a new device certificate from Azure Device Registry.

## What's changed?

The official SDK owns DPS registration, credentials, MQTT transport and retry.
The custom C# security providers, provisioning transports and SAS-token protocol
are removed. `AzureDpsFramework` retains configuration, durable CSR/key/issued
chain files, ADR ARM helpers and a thin unified-session adapter.

Set `RequestOperationalCertificate` to `false` for standard symmetric-key or
X.509 provisioning without issuance; `true` remains the compatibility default
for both CSR flows. .NET 10 and the existing Hub stay unchanged; the SDK discovers
the assigned endpoint/profile through DPS. SDK renewal
APIs are available, but automatic operational-certificate renewal is not implemented.

---

## Configuration

### Symmetric Key Attestation (Default)

```json
"DpsProvisioning": {
  "AttestationMethod": "SymmetricKey",
  "DeviceKeyBase64": "your-device-specific-key-here",
  "RequestOperationalCertificate": true,
  "RegistrationId": "your-device-id",
  "AutoGenerateCsr": true,
  "CsrFilePath": "certs/device.csr",
  "CsrKeyFilePath": "certs/device.key",
  "IssuedCertFilePath": "certs/issued.pem"
}
```

**Flow:**
1. Loads the device-specific key (group-key derivation remains a compatibility option)
2. Supplies the credential and durable CSR/key pair to the unified SDK
3. SDK authenticates to DPS
4. Submits CSR → receives certificate chain from Azure Device Registry
5. Connects to IoT Hub using issued X.509 certificate

---

### X.509 Attestation

```json
"DpsProvisioning": {
  "AttestationMethod": "X509",
  "RequestOperationalCertificate": true,
  "AttestationCertPath": "certs/bootstrap.pem",
  "AttestationKeyPath": "certs/bootstrap.key",
  "AttestationCertChainPath": "",
  "RegistrationId": "your-device-id",
  "AutoGenerateCsr": true,
  "CsrFilePath": "certs/device.csr",
  "CsrKeyFilePath": "certs/device.key",
  "IssuedCertFilePath": "certs/issued.pem"
}
```

**Flow:**
1. Loads existing X.509 certificate for DPS authentication
2. Connects to DPS via MQTT with client certificate (TLS mutual auth)
3. Submits CSR → receives NEW certificate chain from Azure Device Registry
4. Connects to IoT Hub using newly issued X.509 certificate

---

## Generating Self-Signed Certificates for Testing

The `CertificateManager` class now includes a helper method to generate self-signed X.509 certificates for development/testing:

```csharp
var (certPem, keyPem) = CertificateManager.GenerateSelfSignedCertificate(
    commonName: "my-device-01",
    validityDays: 365,
    algorithm: "RSA",  // or "ECDSA"
    rsaKeySize: 2048
);

CertificateManager.SaveText("certs/bootstrap.pem", certPem);
CertificateManager.SaveText("certs/bootstrap.key", keyPem);
```

---

## Azure DPS Enrollment Configuration

### For Symmetric Key Attestation
- Create an **Enrollment Group** with symmetric key attestation
- Prefer a device-specific key in `DeviceKeyBase64`; `EnrollmentGroupKeyBase64` remains a compatibility option

### For X.509 Attestation
- Create an **Individual Enrollment** or **Enrollment Group** with X.509 attestation
- Upload your CA certificate or root certificate to DPS
- Your bootstrap certificate must chain to the uploaded root/CA cert

---

## Use Cases

### Symmetric Key → X.509 CSR
- **Zero-touch provisioning**: Device starts with symmetric key, receives X.509 cert
- **Simple bootstrap**: No need to pre-provision certificates
- **Validation**: Verify the selected profile against your own Azure resources

### X.509 → X.509 CSR  
- **Certificate rotation**: Device uses existing cert to request new one
- **Bootstrap scenarios**: Short-lived bootstrap cert requests long-lived operational cert
- **Dual-layer security**: Pre-existing cert proves identity, ADR issues fresh cert

---

## Architecture Notes

### Unified connection

The SDK `ConnectionClient` owns provisioning and the Hub connection; the
application's `TelemetryClient` shares it. The adapter selects symmetric-key or
X.509 credentials and whether to request operational-certificate issuance.
There are no custom provider or transport classes to configure.

Keep the CSR, matching key and issued chain across retries/restarts, outside
application releases. Paths retain the existing
[configuration resolution rules](../docs/Configuration.md).
Don't overwrite a CSR key while retaining a certificate for the old key.

---

## Testing

Current configuration uses **Symmetric Key** attestation (default). To test X.509 attestation:

1. Generate self-signed bootstrap certificate
2. Create DPS enrollment with X.509 attestation
3. Update `appsettings.json` with `AttestationMethod: "X509"` and certificate paths
4. Run a bounded online check without physical hardware:

   ```powershell
   dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
   ```

This provisions/connects, sends representative telemetry and disconnects
gracefully. `--self-test --config PATH` remains offline.

---

## Official SDK references

- [Pinned preview package](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4)
- [SDK design and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
- [Unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified)

The separate updater still uses its own DPS **software-update** MQTT protocol;
the unified SDK doesn't implement it. Its MQTTnet 4 dependency remains separate
from the sorter's MQTTnet 5 dependency.
