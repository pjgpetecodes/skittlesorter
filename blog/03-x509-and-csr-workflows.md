# Understanding X.509 and CSR Workflows

[← Previous: Creating Azure Resources](02-creating-azure-resources.md) | [Next: Configuring Enrollment Groups →](04-configuring-enrollment-groups.md)

---

In this post, we'll explore the differences between traditional X.509 certificate management and the new Certificate Signing Request (CSR) workflow. Understanding both approaches will help you choose the right strategy for your IoT deployment.

---

## Quick Setup

### Using the Automation Scripts

We provide helper scripts to automate certificate generation and DPS setup:

#### Automated Full Setup (ADR + X.509)

```powershell
cd scripts

.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "eastus" `
  -IoTHubName "my-iothub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adrnamespace-001" `
  -UserIdentity "my-uami" `
  -RegistrationId "my-device" `
  -EnrollmentGroupId "my-device-group" `
  -AttestationType "X509"
```

This generates:
- Root CA (3650 days, pathlen:1)
- Intermediate CA (1825 days, pathlen:0)
- Device bootstrap certificate (365 days)
- Certificate verification in DPS
- Enrollment group with credential policy

#### Automated X.509 Only Setup

```powershell
cd scripts

.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg" `
  -EnrollmentGroupId "my-device-group"
```

Generates just X.509 certificates and performs DPS verification.

---

## What Are X.509 Certificates?

X.509 certificates are digital documents that bind a public key to an identity. Think of them like digital passports:

- **Subject:** Who the certificate belongs to (device ID)
- **Issuer:** Who signed/issued the certificate (Certificate Authority)
- **Public Key:** Used to encrypt data or verify signatures
- **Private Key:** Kept secret, used to decrypt or sign
- **Validity Period:** Start and end dates

## X.509 Bootstrap Certificates for DPS Attestation

To use X.509 authentication with DPS, you need to create and upload a **bootstrap certificate chain** that devices will use to authenticate with DPS during provisioning.

**Important:** These bootstrap certificates are used **only for DPS authentication**. The certificates devices use to connect to IoT Hub are issued by ADR via the CSR-based workflow.

```
┌────────────────┐
│  Your Host     │  Generate Root & Intermediate CA
│  Machine       │  (create your certificate chain)
└────────┬───────┘
         │
         ▼
┌─────────────────────────────────────┐
│  Upload Root/Intermediate to DPS    │
│  (proof of possession)              │
└────────┬────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│  Create Bootstrap Device Certs      │
│  (signed by Root CA)                │
│  (for DPS auth only)                │
└────────┬────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│  Deploy Bootstrap Certs to Devices  │
│  (for DPS provisioning)             │
└─────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│  During Provisioning:               │
│  Device authenticates to DPS        │
│  Device submits CSR                 │
│  ADR issues operational cert        │
└─────────────────────────────────────┘
```

You can either use the automation scripts from "Quick Setup" or follow these manual steps.

---

## Manual Step-by-Step: Create Bootstrap Certificates

### Prerequisites

```powershell
# Verify tools are available
az --version                    # Azure CLI
openssl version                 # OpenSSL
```

### Setup Variables

Before starting, define your environment:

```powershell
$resourceGroup = "my-iot-rg"
$location = "eastus"
$dpsName = "my-dps-001"
$iotHubName = "my-iothub-001"
$adrNamespace = "my-adrnamespace-001"
$userIdentity = "my-uami"
$registrationId = "my-device"
$enrollmentGroupId = "my-device-group"
$credentialPolicyName = "cert-policy"

# Create directory structure for certificates
$certDir = ".\certs"
New-Item -ItemType Directory -Path "$certDir\root" -Force | Out-Null
New-Item -ItemType Directory -Path "$certDir\ca" -Force | Out-Null
New-Item -ItemType Directory -Path "$certDir\device" -Force | Out-Null
New-Item -ItemType Directory -Path "$certDir\issued" -Force | Out-Null
```

### Step 1: Create Your Own Certificate Authority

Now we stand up a root and intermediate CA so DPS can trust a chain you control.

```powershell
# Create root CA private key
openssl genrsa -out "$certDir\root\root-ca.key" 4096

# Create root CA certificate
openssl req -x509 -new -nodes `
  -key "$certDir\root\root-ca.key" `
  -sha256 -days 3650 `
  -out "$certDir\root\root-ca.pem" `
  -subj "/CN=$registrationId-root" `
  -addext "basicConstraints=critical,CA:true,pathlen:1" `
  -addext "keyUsage=critical,keyCertSign,cRLSign"

Write-Host "✓ Root CA created: $certDir\root\root-ca.pem" -ForegroundColor Green

# Create intermediate CA private key
openssl genrsa -out "$certDir\ca\intermediate-ca.key" 4096

# Create intermediate CA CSR
openssl req -new `
  -key "$certDir\ca\intermediate-ca.key" `
  -out "$certDir\ca\intermediate-ca.csr" `
  -subj "/CN=$registrationId-intermediate"

# Create intermediate CA extensions file
@"
[ v3_intermediate ]
basicConstraints = critical,CA:true,pathlen:0
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path "$certDir\ca\intermediate-ext.cnf" -Encoding ASCII

# Sign intermediate CA with root CA
openssl x509 -req `
  -in "$certDir\ca\intermediate-ca.csr" `
  -CA "$certDir\root\root-ca.pem" `
  -CAkey "$certDir\root\root-ca.key" `
  -CAcreateserial `
  -out "$certDir\ca\intermediate-ca.pem" `
  -days 1825 -sha256 `
  -extfile "$certDir\ca\intermediate-ext.cnf" -extensions v3_intermediate

Write-Host "✓ Intermediate CA created: $certDir\ca\intermediate-ca.pem" -ForegroundColor Green
```

### Step 2: Upload and Verify CA in DPS

Next up we prove ownership of that CA to DPS via a verification code cert.

```powershell
$caCertName = "$registrationId-intermediate"

# Upload CA certificate to DPS
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  --path "$certDir\ca\intermediate-ca.pem"

Write-Host "✓ Certificate uploaded to DPS" -ForegroundColor Green

# Get etag for verification
$cert = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  -o json | ConvertFrom-Json -AsHashTable
$etag = $cert.properties.etag

# Generate verification code
$verResponse = az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  --etag $etag `
  -o json | ConvertFrom-Json -AsHashTable
$verificationCode = $verResponse.properties.verificationCode

Write-Host "  Verification Code: $verificationCode" -ForegroundColor Cyan

# Create verification certificate (proof of possession)
openssl genrsa -out "$certDir\ca\verification.key" 2048

openssl req -new `
  -key "$certDir\ca\verification.key" `
  -out "$certDir\ca\verification.csr" `
  -subj "/CN=$verificationCode"

openssl x509 -req `
  -in "$certDir\ca\verification.csr" `
  -CA "$certDir\ca\intermediate-ca.pem" `
  -CAkey "$certDir\ca\intermediate-ca.key" `
  -out "$certDir\ca\verification.pem" `
  -days 30 -sha256

# Update etag before verification
$certCheck = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  -o json | ConvertFrom-Json -AsHashTable
$etag = $certCheck.properties.etag

# Upload verification certificate
az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  --path "$certDir\ca\verification.pem" `
  --etag $etag

# Check verification status
$final = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name $caCertName `
  -o json | ConvertFrom-Json -AsHashTable
$isVerified = $final.properties.isVerified

Write-Host "✓ Certificate verified in DPS (isVerified: $isVerified)" -ForegroundColor Green
```

### Step 3: Create Bootstrap Device Certificates

Create per-device bootstrap certificates signed by your intermediate CA. These certificates will be deployed to devices and used for DPS authentication.

```powershell
# Create device private key (stays on device)
openssl genrsa -out "$certDir\device\device.key" 2048

# Create device CSR
openssl req -new `
  -key "$certDir\device\device.key" `
  -out "$certDir\device\device.csr" `
  -subj "/CN=$registrationId"

# Create device extensions file
@"
[ v3_req ]
basicConstraints = CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = clientAuth
subjectAltName = DNS:$registrationId
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path "$certDir\device\device-ext.cnf" -Encoding ASCII

# Sign device certificate with intermediate CA
openssl x509 -req `
  -in "$certDir\device\device.csr" `
  -CA "$certDir\ca\intermediate-ca.pem" `
  -CAkey "$certDir\ca\intermediate-ca.key" `
  -out "$certDir\device\device.pem" `
  -days 365 -sha256 `
  -extfile "$certDir\device\device-ext.cnf" -extensions v3_req

Write-Host "✓ Bootstrap device certificate created: $certDir\device\device.pem" -ForegroundColor Green

# Verify certificate chain
$verification = openssl verify `
  -CAfile "$certDir\root\root-ca.pem" `
  -untrusted "$certDir\ca\intermediate-ca.pem" `
  "$certDir\device\device.pem" 2>&1

Write-Host "✓ Certificate chain verified: $verification" -ForegroundColor Green

# Create certificate chain for TLS (needed for DPS connection)
Get-Content "$certDir\ca\intermediate-ca.pem", "$certDir\root\root-ca.pem" | `
  Set-Content -Path "$certDir\device\chain.pem" -Encoding ASCII

Write-Host "✓ Certificate chain file created: $certDir\device\chain.pem" -ForegroundColor Green
```

### Step 4: Summary - Bootstrap Certificates Ready

```powershell
Write-Host "`n=== Bootstrap Certificate Setup Complete ===" -ForegroundColor Green

Write-Host "`nBootstrap Certificate Files (for DPS authentication):"
Write-Host "  Root CA: $certDir\root\root-ca.pem"
Write-Host "  Intermediate CA: $certDir\ca\intermediate-ca.pem"
Write-Host "  Device Bootstrap Cert: $certDir\device\device.pem"
Write-Host "  Device Private Key: $certDir\device\device.key"
Write-Host "  Trust Chain: $certDir\device\chain.pem"

Write-Host "`nNext Steps:"
Write-Host "1. Deploy these bootstrap certificates to your device:"
Write-Host "   - AttestationCertPath: $certDir\device\device.pem"
Write-Host "   - AttestationKeyPath: $certDir\device\device.key"
Write-Host "2. Device uses these to authenticate with DPS"
Write-Host "3. During provisioning, device submits CSR"
Write-Host "4. ADR issues operational certificate for IoT Hub"
Write-Host "5. See post 04 to create the enrollment group"
```

---

## Comparing Attestation Approaches



### Step 4: Deploy Certificates to Devices
Then we copy certs + keys onto devices and plan for secure storage and rotation.

- Copy device certificate + private key to each device
- Secure storage required (TPM, secure element, etc.)
- Manual certificate rotation when certificates expire

### Problems with Traditional (Self-Signed) Approach

❌ **Operational:** Keys must be pre-generated and securely deployed to every device  
❌ **Security Risk:** Private keys are transported during setup, increasing exposure  
❌ **Rotation:** Manual process to renew expiring bootstrap certificates  
❌ **Scaling:** Each new device requires pre-configuration  

## New CSR-Based Workflow (Microsoft-Managed CA)

The new approach flips the model: devices generate their own keys and request certificates during provisioning.

### How CSR Works

**Certificate Signing Request (CSR)** contains:
- Device's public key
- Device identity information (Subject, Common Name)
- Signature proving possession of private key

**The device:**
1. Generates a private key (stays on device, never leaves)
2. Generates a CSR using the private key
3. Sends CSR to DPS during provisioning
4. Receives signed certificate back

### Step-by-Step: Device Perspective
Now we flip to the CSR model: the device makes its own key, asks DPS/ADR to sign it.

```
┌─────────────────────────┐
│      Device (Boot)      │
│                         │
│  Generate RSA/ECC Key   │
│  (private key stays)    │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Build CSR              │
│  (CN=registration_id)   │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Connect to DPS         │
│  (symmetric key auth)   │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Submit CSR to DPS      │
│  (Base64-encoded DER)   │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  DPS validates CSR      │
│  (proves ownership)     │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  ADR signs CSR          │
│  (issues certificate)   │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Device receives cert   │
│  + certificate chain    │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Combine cert +         │
│  private key (PFX)      │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Connect to IoT Hub     │
│  (X.509 TLS)            │
└─────────────────────────┘
```

**Key difference:** Private key is **generated and stays on the device** - never transmitted to DPS or any other service.

### Advantages of CSR Approach

✅ **No CA Management:** Microsoft manages the Certificate Authority  
✅ **Private Key Security:** Keys generated on device, never transmitted  
✅ **Automated:** Certificates issued during provisioning  
✅ **Scalable:** Works for millions of devices  
✅ **Lifecycle Management:** ADR credential policies handle renewal  
✅ **Zero-Touch:** Device provisions itself on first boot  

## Comparing Attestation Approaches

| Feature | Bootstrap X.509 (DPS only) | CSR-Based (IoT Hub) |
|---------|--------------------------|-------------------|
| **Purpose** | Device authenticates to DPS | Device authenticates to IoT Hub |
| **Issued By** | You (self-signed CA) | Microsoft (ADR) |
| **Generated On** | Host machine | Device itself |
| **Private Key** | Pre-deployed | Never leaves device |
| **Lifecycle** | Manual renewal | Automatic (ADR policies) |
| **Risk** | Keys must be securely deployed | Minimal (no key transport) |

## Dual Attestation Pattern

One powerful pattern: **authenticate with symmetric key, receive X.509 certificate**.

```
┌──────────────────────────────────┐
│     PHASE 1: PROVISIONING        │
│     (Symmetric Key + CSR)        │
└──────────┬───────────────────────┘
           │
    ┌──────┴──────┐
    │             │
    ▼             ▼
┌────────┐  ┌──────────────┐
│ Device │  │ Enrollment   │
│ Derives│  │ Group Key    │
│ Key    │  │              │
└────┬───┘  └──────┬───────┘
     │             │
     │             │
     └──────┬──────┘
            ▼
      ┌──────────────┐
      │ Generate SAS │
      │ Token        │
      └───────┬──────┘
              ▼
      ┌──────────────────┐
      │ Connect to DPS   │
      │ (Token Auth)     │
      └───────┬──────────┘
              ▼
      ┌──────────────────┐
      │ Submit CSR       │
      │ Generate Cert    │
      └───────┬──────────┘
              ▼
      ┌──────────────────┐
      │ Receive X.509    │
      │ Certificate      │
      └───────┬──────────┘
              │
┌─────────────┴──────────────┐
│                            │
▼                            ▼
┌──────────────────────────────────┐
│     PHASE 2: OPERATION           │
│     (X.509 Certificate Auth)     │
└──────────┬───────────────────────┘
           │
           ▼
      ┌──────────────────┐
      │ Connect to IoT   │
      │ Hub using X.509  │
      │ (TLS 1.2+)       │
      └──────────────────┘
```

### Why this pattern?

- ✅ **Easy provisioning:** Symmetric keys are simple, no pre-generated certs needed
- ✅ **Secure operation:** X.509 is stronger than SAS tokens for long-lived connections
- ✅ **Best of both worlds:** Use what's convenient for setup, use what's secure for operation

**Why this pattern?**
- ✅ Easy provisioning (no pre-generated certs needed)
- ✅ Secure operation (X.509 is stronger than SAS)
- ✅ Best of both worlds

## Certificate Chain Structure

When DPS issues a certificate, it returns a **certificate chain**:

```
┌─────────────────────────────┐
│  Root CA Certificate        │  (Microsoft-managed)
└──────────┬──────────────────┘
           │ Signs
           ▼
┌─────────────────────────────┐
│  Intermediate CA Certificate│  (Microsoft-managed)
└──────────┬──────────────────┘
           │ Signs
           ▼
┌─────────────────────────────┐
│  Device Certificate         │  (Your device)
└─────────────────────────────┘
```

**Device must install the full chain:**

The response from DPS contains an array of Base64-encoded certificates:
- **[0]** = Device certificate (the one issued by ADR)
- **[1]** = Intermediate CA certificate  
- **[2]** = Root CA certificate (optional, often in system trust store)

The device must:
1. Decode the certificate from Base64
2. Combine it with the private key that was used to create the CSR
3. Export the combined cert+key to a format for persistent storage (PFX on Windows, PEM on Linux)
4. Use this cert+key pair for all future IoT Hub connections

## When to Use Which Approach?

### Use Traditional X.509 When:
- ✅ You already have PKI infrastructure
- ✅ Regulatory requirements for specific CA
- ✅ Offline provisioning required (no internet during setup)
- ✅ Certificates must be pre-installed at factory

### Use CSR-Based Approach When:
- ✅ New IoT deployment
- ✅ Want zero-touch provisioning
- ✅ Don't want to manage CA infrastructure
- ✅ Need automated certificate lifecycle
- ✅ Maximum security (keys never leave device)

## Certificate Lifecycle with ADR

ADR credential policies automate certificate management:

```json
{
  "name": "cert-policy",
  "type": "x509CA",
  "validity": "P30D",        // 30 days
  "renewalWindow": "P7D"     // Renew 7 days before expiry
}
```

**Automatic Renewal Flow:**

```
Day 0:   Device provisions, receives certificate (valid 30 days)
Day 23:  Renewal window opens (7 days before expiry)
Day 23:  Device checks for renewal (via ADR API)
Day 23:  New certificate issued automatically
Day 30:  Old certificate expires (but already replaced)
```

**Renewal logic:**

The device should periodically check if its certificate is within the renewal window (typically 7 days before expiry). When renewal is needed, the device generates a new CSR and submits it to ADR to receive a new certificate.

```
     Day 0              Day 23              Day 30
     ↓                  ↓                   ↓
┌──────────────────────────────────────────────────────┐
│ Certificate Lifecycle (30-day validity)              │
├──────────────────────────────────────────────────────┤
│                                                      │
│  [Valid]          [Renewal Window]    [Expired]      │
│                   (7 days before)                    │
│                                                      │
│  Day 0-22         Day 23-29           Day 30+        │
│  ✓ Using          ↓ Check for         ✗ Invalid      │
│    Old Cert       renewal             (shouldn't     │
│                   ↓ Generate          reach here)    │
│                     new CSR                          │
│                   ↓ Request new cert                 │
│                   ↓ Receive & save                   │
│                   ✓ New Cert Ready                   │
│                                                      │
└──────────────────────────────────────────────────────┘
```

## CSR Generation Deep Dive

### RSA vs ECC

**RSA (Rivest-Shamir-Adleman):**
- Traditional, widely supported
- 2048-bit or 4096-bit keys
- Larger key size = slower operations, higher overhead on IoT devices
- Fine for devices with sufficient computing power

**ECC (Elliptic Curve Cryptography):**
- Modern, more efficient
- 256-bit provides equivalent security to RSA 3072-bit
- Smaller keys, faster operations, lower power consumption
- **Recommended for resource-constrained IoT devices**

### CSR Format

CSR must be in **Base64-encoded DER format** (not PEM headers/footers).

The registration payload sent to DPS looks like:
```json
{
  "registrationId": "device-001",
  "csr": "MIICXTCCAUUCAQAwGDEWMBQGA1UEAxMNZGV2aWNlLTAwMTCCAS..."
}
```

The CSR value is the raw DER bytes, base64-encoded, without the `-----BEGIN CERTIFICATE REQUEST-----` headers.

## Security Best Practices

### Private Key Storage

```
    ✅ GOOD                      ❌ BAD
                                
    Device                       Host Machine
       ↓                             ↓
  ┌────────────┐             ┌──────────────┐
  │ Generate   │             │ Generate Key │
  │ Private    │             │              │
  │ Key        │             └──────┬───────┘
  └────┬───────┘                    │
       │                            ▼
       ▼                     ┌──────────────┐
  ┌────────────┐             │ Store in DB  │
  │ TPM/Secure │             │ or Config    │
  │ Element    │             │ (Risk!)      │
  │ (Hardware) │             └──────┬───────┘
  └────┬───────┘                    │
       │                            ▼
       ▼                      Transfer
  ┌────────────┐             Network
  │ Encrypted  │             (Exposed!)
  │ at Rest    │                    │
  │ (Optional) │                    ▼
  └────┬───────┘             ┌──────────────┐
       │                     │ Device       │
       └──────────┬──────────┤ (Late)       │
                  │          └──────────────┘
                  ▼
             ┌─────────┐
             │ Use Key │
             │ Locally │
             └─────────┘
```

**Do:**
- ✅ Generate keys on device (never transmit)
- ✅ Use hardware security modules (TPM, secure element)
- ✅ Encrypt at rest if storing in filesystem
- ✅ Use strong file permissions (chmod 600)

**Don't:**
- ❌ Generate keys on a server and transfer to device
- ❌ Store unencrypted in filesystem
- ❌ Hardcode in source code
- ❌ Log or transmit private keys

### Certificate Validation

When using a certificate to connect to IoT Hub, verify:
- ✅ Certificate has not expired (NotAfter > current time)
- ✅ Certificate subject matches the device ID
- ✅ Certificate chain is valid (IoT Hub trusts the issuer)
- ✅ Certificate was issued by the expected CA

## Testing Certificate Workflows

For testing CSR generation:
- Generate a CSR on your device using OpenSSL or your cryptography library
- Inspect it with: `openssl req -text -noout -in device.csr`
- Verify the subject (CN) matches your registration ID
- Verify the public key is present and correct
- Submit to DPS and verify the returned certificate chain

## Next Steps

Now that you understand certificate workflows, we'll configure enrollment groups to use these certificates:
- Creating symmetric key enrollment groups
- Creating X.509 enrollment groups  
- Linking credential policies for CSR issuance
- Testing enrollment configurations

---

[Next: Configuring Enrollment Groups →](04-configuring-enrollment-groups.md)
