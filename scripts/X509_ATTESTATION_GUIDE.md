# X.509 Attestation Support for CSR-Based Provisioning

## Overview

Your DPS framework now supports **two attestation methods** for CSR-based certificate provisioning:

1. **Symmetric Key Attestation** (existing) - Uses SAS token for DPS authentication
2. **X.509 Attestation** (new) - Uses existing X.509 certificate for DPS authentication

Both methods submit a Certificate Signing Request (CSR) to receive a new device certificate from Azure Device Registry.

---

## Configuration

### Symmetric Key Attestation (Default)

```json
"DpsProvisioning": {
  "AttestationMethod": "SymmetricKey",
  "EnrollmentGroupKeyBase64": "your-enrollment-group-key-here",
  "RegistrationId": "your-device-id",
  "AutoGenerateCsr": true,
  "CsrFilePath": "certs/device.csr",
  "CsrKeyFilePath": "certs/device.key",
  "IssuedCertFilePath": "certs/issued.pem"
}
```

**Flow:**
1. Derives device key from enrollment group key
2. Generates SAS token for DPS authentication
3. Connects to DPS via MQTT with SAS token
4. Submits CSR → receives certificate chain from Azure Device Registry
5. Connects to IoT Hub using issued X.509 certificate

---

### X.509 Attestation (New)

```json
"DpsProvisioning": {
  "AttestationMethod": "X509",
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
- Provide the primary key in `EnrollmentGroupKeyBase64`

### For X.509 Attestation
- Create an **Individual Enrollment** or **Enrollment Group** with X.509 attestation
- Upload your CA certificate or root certificate to DPS
- Your bootstrap certificate must chain to the uploaded root/CA cert

---

## Use Cases

### Symmetric Key → X.509 CSR
- **Zero-touch provisioning**: Device starts with symmetric key, receives X.509 cert
- **Simple bootstrap**: No need to pre-provision certificates
- **Current implementation**: Working and tested ✅

### X.509 → X.509 CSR  
- **Certificate rotation**: Device uses existing cert to request new one
- **Bootstrap scenarios**: Short-lived bootstrap cert requests long-lived operational cert
- **Dual-layer security**: Pre-existing cert proves identity, ADR issues fresh cert

---

## Architecture Notes

### Security Providers

| Provider | Attestation | CSR Support | Use Case |
|----------|-------------|-------------|----------|
| `SecurityProviderSymmetricKey` | SAS token | ❌ | Standard symmetric key (no CSR) |
| `SecurityProviderX509Csr` | SAS token | ✅ | Symmetric key attestation + CSR for cert issuance |
| `SecurityProviderX509CsrWithCert` | X.509 cert | ✅ | X.509 attestation + CSR for cert issuance |
| `SecurityProviderX509Certificate` | X.509 cert | ❌ | Standard X.509 (stub, not implemented) |

### Transport Layer

The `ProvisioningTransportHandlerMqtt` class now detects the security provider type and configures MQTT accordingly:

- **Symmetric Key**: Uses `.WithCredentials(username, sasToken)`
- **X.509**: Uses `.WithClientCertificates(authCert)` and empty password

### Preview API

When a CSR is present, the framework automatically uses the **2025-07-01-preview** API version for Azure Device Registry certificate issuance.

---

## Testing

Current configuration uses **Symmetric Key** attestation (default). To test X.509 attestation:

1. Generate self-signed bootstrap certificate
2. Create DPS enrollment with X.509 attestation
3. Update `appsettings.json` with `AttestationMethod: "X509"` and certificate paths
4. Run application

---

## Alignment with Official SDK

This implementation aligns with the Microsoft Azure Device Provisioning SDK patterns:

- ✅ `SecurityProvider` abstract base class
- ✅ `SecurityProviderX509` intermediate base with `GetAuthenticationCertificate()` / `GetAuthenticationCertificateChain()`
- ✅ `ProvisioningTransportHandler` abstract base with `RegisterAsync()` overloads
- ✅ `ProvisioningDeviceClient.Create()` factory pattern
- ✅ `ProvisioningTransportRegisterMessage` wrapper

The CSR-based provisioning functionality is a **PREVIEW extension** not yet available in the official SDK.
