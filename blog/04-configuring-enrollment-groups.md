# Configuring Enrollment Groups

[← Previous: Understanding X.509 and CSR Workflows](03-x509-and-csr-workflows.md) | [Next: Building the Custom DPS Framework →](05-building-dps-framework.md)

---

In this post, we'll configure Device Provisioning Service (DPS) enrollment groups that use Azure Device Registry (ADR) credential policies for certificate issuance. We'll focus on **X.509 attestation**, while still noting **symmetric key** as an option.

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
| **X.509 Certificate** | Certificate-based attestation | Production, high-security environments |
| **TPM** | Hardware security module | Devices with TPM chips |
| **Symmetric Key** | Shared secret attestation | Development, testing, simple devices |

This post covers **X.509** with CSR-based certificate issuance. Symmetric key is an alternative option, but steps are not included here.

## Prerequisites

Before proceeding, ensure you've completed:
- [Creating Azure Resources](02-creating-azure-resources.md) - DPS, IoT Hub, and ADR setup
- ADR credential policy created (`cert-policy`)
- DPS linked to IoT Hub and ADR namespace

## X.509 Certificate Attestation with CSR

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

### Step 1: Ensure CA is Uploaded and Verified

This was completed in [Post 03: X.509 and CSR Workflows](03-x509-and-csr-workflows.md). If you skipped that post, complete the CA upload and verification steps there before continuing.

**Quick checks (confirm both root and intermediate are verified):**

**Set variables:**

```powershell
$dpsName = "my-dps-001"
$resourceGroup = "my-iot-rg"
$registrationId = "my-device"

$rootCertName = "$registrationId-root"
$intermediateCertName = "$registrationId-intermediate"
```

**Check root CA verification status:**

```powershell
az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $rootCertName `
  --query properties.isVerified -o tsv
```

**Check intermediate CA verification status:**

```powershell
az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $intermediateCertName `
  --query properties.isVerified -o tsv
```

### Step 2: Create X.509 Enrollment Group

**Set variables for enrollment:**

```powershell
$enrollmentGroupName = "my-device-group"
$iotHubName = "my-iothub-001"
$credentialPolicyName = "cert-policy"
```

**Create enrollment group:**

```powershell
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "${enrollmentGroupName}-x509" `
  --attestation-type x509 `
  --ca-name "$registrationId-intermediate" `
  --iot-hub-host-name "$iotHubName.azure-devices.net" `
  --provisioning-status enabled `
  --credential-policy $credentialPolicyName `
  --edge-enabled false
```

### Step 3: Configuration for Device

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "my-device",
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

✅ Created enrollment groups for X.509 attestation  
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
