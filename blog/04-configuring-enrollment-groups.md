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

## Option 2: X.509 Certificate Attestation with CSR

This approach uses an **existing X.509 certificate** (bootstrap certificate) for DPS authentication, then receives a **new certificate** for IoT Hub communication.

### Use Case

- Production environments requiring certificate-based authentication end-to-end
- Devices with pre-installed factory certificates
- Regulatory requirements for PKI

### Step 1: Create or Obtain Bootstrap Certificates

You need a **Certificate Authority (CA)** that you control:

```bash
# Create CA private key
openssl genrsa -out ca.key 4096

# Create CA certificate
openssl req -x509 -new -nodes \
  -key ca.key \
  -sha256 -days 3650 \
  -out ca.pem \
  -subj "/CN=Skittle Sorter CA"

# Create device bootstrap certificate
openssl genrsa -out bootstrap.key 2048

openssl req -new \
  -key bootstrap.key \
  -out bootstrap.csr \
  -subj "/CN=dev001-skittlesorter"

openssl x509 -req \
  -in bootstrap.csr \
  -CA ca.pem \
  -CAkey ca.key \
  -CAcreateserial \
  -out bootstrap.pem \
  -days 365 -sha256

# Combine into PFX for device use
openssl pkcs12 -export \
  -out bootstrap.pfx \
  -inkey bootstrap.key \
  -in bootstrap.pem \
  -password pass:yourpassword
```

### Step 2: Upload CA Certificate to DPS

```powershell
# Upload CA certificate
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --name "SkittleSorterCA" `
  --path "./ca.pem"
```

### Step 3: Verify CA Certificate (Proof of Possession)

DPS requires proof that you control the private key:

```powershell
# Generate verification code
$verificationCode = az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "SkittleSorterCA" `
  --etag (az iot dps certificate show `
    --dps-name $dpsName `
    --resource-group $resourceGroup `
    --certificate-name "SkittleSorterCA" `
    --query etag -o tsv) `
  --query properties.verificationCode -o tsv

Write-Host "Verification Code: $verificationCode"
```

Create verification certificate:

```bash
# Generate verification certificate
openssl genrsa -out verification.key 2048

openssl req -new \
  -key verification.key \
  -out verification.csr \
  -subj "/CN=$verificationCode"

openssl x509 -req \
  -in verification.csr \
  -CA ca.pem \
  -CAkey ca.key \
  -out verification.pem \
  -days 30 -sha256
```

Upload verification certificate:

```powershell
az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "SkittleSorterCA" `
  --path "./verification.pem" `
  --etag (az iot dps certificate show `
    --dps-name $dpsName `
    --resource-group $resourceGroup `
    --certificate-name "SkittleSorterCA" `
    --query etag -o tsv)
```

### Step 4: Create X.509 Enrollment Group

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

### Step 5: Configuration for Device

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "dev001-skittlesorter",
    "AttestationMethod": "X509",
    "AttestationCertPath": "./certs/bootstrap.pfx",
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
5. Receive assigned IoT Hub + **new** X.509 certificate
6. Connect to IoT Hub using **new** certificate

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

## Adding Custom Device Properties

You can set initial device twin properties in enrollment groups:

```powershell
# Create enrollment group with initial twin state
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type symmetricKey `
  --iot-hub-host-name "$iotHubName.azure-devices.net" `
  --provisioning-status enabled `
  --credential-policy $credentialPolicyName `
  --initial-twin-tags '{"location":"factory-1","deviceType":"skittle-sorter"}' `
  --initial-twin-properties '{"telemetryInterval":5000,"enableSorting":true}'
```

Device twin will be initialized with:

```json
{
  "tags": {
    "location": "factory-1",
    "deviceType": "skittle-sorter"
  },
  "properties": {
    "desired": {
      "telemetryInterval": 5000,
      "enableSorting": true
    }
  }
}
```

## Custom Allocation Policies

You can implement custom logic to assign devices to different IoT Hubs:

```powershell
# Create Azure Function for custom allocation
# (Function code would determine target hub based on device attributes)

# Link function to enrollment group
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type symmetricKey `
  --allocation-policy custom `
  --webhook-url "https://your-function-app.azurewebsites.net/api/allocate" `
  --api-version "2019-03-31" `
  --credential-policy $credentialPolicyName
```

Custom allocation examples:
- Route devices by geographic location
- Load balance across multiple IoT Hubs
- Assign based on tenant ID (multi-tenancy)

## Viewing and Managing Enrollment Groups

### List All Enrollment Groups

```powershell
az iot dps enrollment-group list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --output table
```

### View Enrollment Group Details

```powershell
az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName
```

### Update Enrollment Group

```powershell
# Disable provisioning
az iot dps enrollment-group update `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --provisioning-status disabled

# Change credential policy
az iot dps enrollment-group update `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --credential-policy "new-cert-policy"
```

### Delete Enrollment Group

```powershell
az iot dps enrollment-group delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName
```

## Testing Enrollment Configuration

### Verify Credential Policy Link

```powershell
# Check that enrollment group is linked to credential policy
$enrollment = az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName | ConvertFrom-Json

Write-Host "Credential Policy: $($enrollment.credentialPolicy)"
```

### Test Device Key Derivation

```powershell
# Derive a test device key
$testRegistrationId = "test-device-001"
$testDeviceKey = Get-DeviceKey -enrollmentKey $enrollmentKey -registrationId $testRegistrationId

Write-Host "Test Device: $testRegistrationId"
Write-Host "Derived Key: $testDeviceKey"
```

## Security Best Practices

### Symmetric Key Attestation

✅ **Do:**
- Store enrollment group key in secure configuration (Key Vault, environment variables)
- Derive device-specific keys on device (don't pre-generate)
- Rotate enrollment group keys periodically
- Use different enrollment groups for dev/staging/prod

❌ **Don't:**
- Hardcode enrollment keys in source code
- Share device-derived keys between devices
- Log enrollment or device keys
- Use the same enrollment group for all environments

### X.509 Certificate Attestation

✅ **Do:**
- Store CA private key securely (offline, HSM)
- Use strong key sizes (RSA 2048+, ECC 256+)
- Set appropriate certificate validity periods
- Implement certificate revocation checks

❌ **Don't:**
- Store CA private key in source control
- Use self-signed device certificates without CA chain
- Skip proof of possession verification
- Reuse device certificates across devices

## Troubleshooting

### Error: "Credential policy not found"

**Cause:** ADR credential policy doesn't exist or name mismatch.

**Solution:**
```powershell
# List available credential policies
az iot hub device-identity credential-policy list `
  --namespace $adrNamespace `
  --resource-group $resourceGroup
```

### Error: "IoT Hub not linked"

**Cause:** DPS not properly linked to IoT Hub.

**Solution:**
```powershell
# List linked hubs
az iot dps linked-hub list `
  --dps-name $dpsName `
  --resource-group $resourceGroup

# Re-link if necessary (see previous post)
```

### Device Provisioning Fails

**Debug steps:**
```powershell
# Check DPS device registrations
az iot dps registration list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName

# Check IoT Hub devices
az iot hub device-identity list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

## What We Accomplished

✅ Created symmetric key enrollment group with credential policy  
✅ Learned to derive device-specific keys  
✅ Configured X.509 certificate attestation (optional)  
✅ Verified proof of possession for X.509 CA  
✅ Set up initial device twin properties  
✅ Ready to implement device provisioning code  

## Next Steps

Now that enrollment groups are configured, we'll build the **custom DPS framework** that implements:
- MQTT protocol communication with DPS
- CSR generation and submission
- Certificate issuance workflow
- Support for preview API features (`2025-07-01-preview`)

---

[Next: Building the Custom DPS Framework →](05-building-dps-framework.md)
