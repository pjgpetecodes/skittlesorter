# Configuring Enrollment Groups

[← Previous: Understanding X.509 and CSR Workflows](03-x509-and-csr-workflows.md) | [Next: Building the Custom DPS Framework →](05-building-dps-framework.md)

---

In this post, we'll configure Device Provisioning Service (DPS) enrollment groups that use Azure Device Registry (ADR) credential policies for certificate issuance. We'll cover both symmetric key and X.509 attestation methods.

## What Are Enrollment Groups?

Enrollment groups define **how** devices authenticate to DPS and **what** happens when they provision:

- **Attestation Method:** How devices prove identity (symmetric key, TPM, or X.509)
- **Target IoT Hub:** Where devices get assigned
- **Initial Twin State:** Default device twin properties
- **Credential Policy:** Which ADR policy to use for certificate issuance

Think of enrollment groups as "templates" for device provisioning.

## Enrollment Group Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Symmetric Key** | Shared secret attestation | Development, testing, simple devices |
| **X.509 Certificate** | Certificate-based attestation | Production, high-security environments |
| **TPM** | Hardware security module | Devices with TPM chips |

This post covers **Symmetric Key** and **X.509** with CSR-based certificate issuance.

## Prerequisites

Before proceeding, ensure you've completed:
- [Creating Azure Resources](02-creating-azure-resources.md) - DPS, IoT Hub, and ADR setup
- ADR credential policy created (`cert-policy`)
- DPS linked to IoT Hub and ADR namespace

## Option 1: Symmetric Key Attestation with CSR

This is the **recommended approach for this project** because it balances simplicity and security:
- **Phase 1 (Provisioning):** Device authenticates with symmetric key
- **Phase 2 (Operation):** Device uses X.509 certificate issued by DPS

### Step 1: Create Enrollment Group

```powershell
# Variables (from previous post)
$dpsName = "dev001-skittlesorter-dps"
$resourceGroup = "dev001-skittlesorter-rg"
$iotHubName = "dev001-skittlesorter-hub"
$enrollmentGroupName = "dev001-skittlesorter-group"
$credentialPolicyName = "cert-policy"

# Create symmetric key enrollment group with credential policy
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type symmetricKey `
  --iot-hub-host-name "$iotHubName.azure-devices.net" `
  --provisioning-status enabled `
  --credential-policy $credentialPolicyName `
  --edge-enabled false
```

**Key parameters:**
- `--attestation-type symmetricKey` - Use symmetric key for DPS authentication
- `--credential-policy $credentialPolicyName` - Link to ADR policy for certificate issuance
- `--edge-enabled false` - Standard IoT device (not IoT Edge)

### Step 2: Retrieve Enrollment Group Key

```powershell
# Get the primary key
$enrollmentKey = az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --query attestation.symmetricKey.primaryKey -o tsv

Write-Host "Enrollment Group Primary Key:" -ForegroundColor Cyan
Write-Host $enrollmentKey
```

> **Important:** Save this key securely! You'll need it in your device configuration.

### Step 3: Derive Device Key

Each device derives its own unique key from the enrollment group key:

```csharp
using System.Security.Cryptography;
using System.Text;

public static string DeriveDeviceKey(string enrollmentGroupKey, string registrationId)
{
    byte[] keyBytes = Convert.FromBase64String(enrollmentGroupKey);
    using var hmac = new HMACSHA256(keyBytes);
    byte[] deviceKeyBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(registrationId));
    return Convert.ToBase64String(deviceKeyBytes);
}

// Example usage
string enrollmentKey = "your-enrollment-group-key";
string registrationId = "dev001-skittlesorter";
string deviceKey = DeriveDeviceKey(enrollmentKey, registrationId);
```

**PowerShell equivalent:**

```powershell
function Get-DeviceKey {
    param(
        [string]$enrollmentKey,
        [string]$registrationId
    )
    
    $keyBytes = [Convert]::FromBase64String($enrollmentKey)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $keyBytes
    $deviceKeyBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($registrationId))
    return [Convert]::ToBase64String($deviceKeyBytes)
}

$deviceKey = Get-DeviceKey -enrollmentKey $enrollmentKey -registrationId "dev001-skittlesorter"
Write-Host "Device Key: $deviceKey"
```

### Step 4: Configuration for Device

Your device application needs these values:

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "dev001-skittlesorter",
    "AttestationMethod": "SymmetricKey",
    "EnrollmentGroupKeyBase64": "your-enrollment-group-key-here",
    "ProvisioningHost": "global.azure-devices-provisioning.net"
  }
}
```

The device will:
1. Derive its unique key using `HMACSHA256`
2. Generate a SAS token for DPS authentication
3. Generate a CSR (Certificate Signing Request)
4. Submit registration with CSR to DPS
5. Receive assigned IoT Hub + X.509 certificate
6. Connect to IoT Hub using X.509 certificate

**Configuration values you'll need:**
- `IdScope`: From DPS overview page
- `RegistrationId`: Your device name
- `EnrollmentGroupKeyBase64`: The primary key retrieved in Step 2

## Option 2: X.509 Certificate Attestation with CSR

This approach uses an **existing X.509 certificate** (bootstrap certificate) for DPS authentication, then receives a **new certificate** for IoT Hub communication.

### Use Case

- Production environments requiring certificate-based authentication end-to-end
- Devices with pre-installed factory certificates
- Regulatory requirements for PKI

### Prerequisites

You should have already created bootstrap certificates in the previous post. If not, follow the manual steps in [Post 03: X.509 and CSR Workflows](03-x509-and-csr-workflows.md) or use the automation script:

```powershell
.\scripts\setup-x509-attestation.ps1 `
  -RegistrationId "dev001-skittlesorter" `
  -DpsName "dev001-skittlesorter-dps" `
  -ResourceGroup "dev001-skittlesorter-rg" `
  -EnrollmentGroupId "dev001-skittlesorter-group"
```

### Step 1: Upload CA Certificate to DPS

If you used the automation script, this is already done. Otherwise:

```powershell
# Variables
$dpsName = "dev001-skittlesorter-dps"
$resourceGroup = "dev001-skittlesorter-rg"
$caCertName = "SkittleSorterCA"

# Upload CA certificate
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --name $caCertName `
  --path "./certs/ca/intermediate-ca.pem"
```

### Step 2: Verify CA Certificate (Proof of Possession)

DPS requires proof that you control the private key:

```powershell
# Get etag
$cert = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  -o json | ConvertFrom-Json

# Generate verification code
$verResponse = az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  --etag $cert.etag `
  -o json | ConvertFrom-Json

$verificationCode = $verResponse.properties.verificationCode

# Create verification certificate
openssl genrsa -out verification.key 2048
openssl req -new -key verification.key -out verification.csr -subj "/CN=$verificationCode"
openssl x509 -req -in verification.csr -CA ./certs/ca/intermediate-ca.pem -CAkey ./certs/ca/intermediate-ca.key -out verification.pem -days 30 -sha256

# Verify
$certCheck = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  -o json | ConvertFrom-Json

az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  --path "./verification.pem" `
  --etag $certCheck.etag
```

### Step 3: Create X.509 Enrollment Group

```powershell
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "${enrollmentGroupName}-x509" `
  --attestation-type x509 `
  --ca-name "SkittleSorterCA" `
  --iot-hub-host-name "$iotHubName.azure-devices.net" `
  --provisioning-status enabled `
  --credential-policy $credentialPolicyName `
  --edge-enabled false
```

### Step 4: Configuration for Device

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "dev001-skittlesorter",
    "AttestationMethod": "X509",
    "AttestationCertPath": "./certs/device/device.pem",
    "AttestationCertPassword": "yourpassword",
    "ProvisioningHost": "global.azure-devices-provisioning.net"
  }
}
```

The device will:
1. Load bootstrap X.509 certificate
2. Connect to DPS with TLS client authentication
3. Generate a new CSR for operational certificate
4. Submit registration with CSR to DPS
5. Receive assigned IoT Hub + new X.509 certificate
6. Connect to IoT Hub using new certificate

**Configuration values you'll need:**
- `IdScope`: From DPS overview page
- `RegistrationId`: Your device name (CN in certificate)
- `AttestationCertPath`: Path to bootstrap certificate (.pem or .pfx)
- `AttestationCertPassword`: Password for certificate (if encrypted)

## Comparing Both Attestation Methods

| Feature | Symmetric Key | X.509 Bootstrap |
|---------|--------------|-----------------|
| **Initial Auth** | Shared secret (HMACSHA256) | Certificate (PKI) |
| **Setup Complexity** | Low | High (CA required) |
| **Security** | Medium | High |
| **Bootstrap Credentials** | Single enrollment group key | Certificate per device |
| **Operational Certificate** | Issued by DPS/ADR | Issued by DPS/ADR |
| **Best For** | Development, simple devices | Production, high security |

**Both methods result in X.509 certificate for IoT Hub communication!**

## What We Accomplished

✅ Created enrollment groups for both symmetric key and X.509 attestation  
✅ Retrieved configuration values needed for device application  
✅ Understood the trade-offs between attestation methods  
✅ Ready to implement device provisioning code

## Next Steps

Now that enrollment groups are configured, we'll build the **custom DPS framework** that implements:
- MQTT protocol communication with DPS
- CSR generation and submission
- Certificate issuance workflow
- Support for preview API features (`2025-07-01-preview`)

---

[Next: Building the Custom DPS Framework →](05-building-dps-framework.md)
