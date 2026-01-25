# Understanding X.509 and CSR Workflows

[← Previous: Creating Azure Resources](02-creating-azure-resources.md) | [Next: Configuring Enrollment Groups →](04-configuring-enrollment-groups.md)

---

In this post, we'll explore the differences between traditional X.509 certificate management and the new Certificate Signing Request (CSR) workflow. Understanding both approaches will help you choose the right strategy for your IoT deployment.

## What Are X.509 Certificates?

X.509 certificates are digital documents that bind a public key to an identity. Think of them like digital passports:

- **Subject:** Who the certificate belongs to (device ID)
- **Issuer:** Who signed/issued the certificate (Certificate Authority)
- **Public Key:** Used to encrypt data or verify signatures
- **Private Key:** Kept secret, used to decrypt or sign
- **Validity Period:** Start and end dates

## Traditional X.509 Workflow (Self-Managed CA)

This is what you'd do **before** Microsoft certificate management:

### Step 1: Create Your Own Certificate Authority
Now we stand up a root and intermediate CA so DPS can trust a chain you control.

```bash
# Create root CA private key
openssl genrsa -out root-ca.key 4096

# Create root CA certificate
openssl req -x509 -new -nodes \
  -key root-ca.key \
  -sha256 -days 3650 \
  -out root-ca.pem \
  -subj "/CN=My IoT Root CA"

# Create intermediate CA private key
openssl genrsa -out intermediate-ca.key 4096

# Create intermediate CA CSR
openssl req -new \
  -key intermediate-ca.key \
  -out intermediate-ca.csr \
  -subj "/CN=My IoT Intermediate CA"

# Sign intermediate CA with root CA
openssl x509 -req \
  -in intermediate-ca.csr \
  -CA root-ca.pem \
  -CAkey root-ca.key \
  -CAcreateserial \
  -out intermediate-ca.pem \
  -days 1825 -sha256
```

### Step 2: Upload and Verify CA in DPS
Next up we prove ownership of that CA to DPS via a verification code cert.

```powershell
# Upload CA certificate to DPS
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --name "MyIoTCA" `
  --path "./intermediate-ca.pem"

# Generate verification code
$verificationCode = az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "MyIoTCA" `
  --query properties.verificationCode -o tsv

# Create verification certificate (proof of possession)
openssl genrsa -out verification.key 2048
openssl req -new \
  -key verification.key \
  -out verification.csr \
  -subj "/CN=$verificationCode"

openssl x509 -req \
  -in verification.csr \
  -CA intermediate-ca.pem \
  -CAkey intermediate-ca.key \
  -out verification.pem \
  -days 30 -sha256

# Upload verification certificate
az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "MyIoTCA" `
  --path "./verification.pem"
```

### Step 3: Generate Device Certificates
Here we mint per-device certs from your intermediate CA—one for every device.

```bash
# For EACH device, generate a certificate
openssl genrsa -out device-001.key 2048

openssl req -new \
  -key device-001.key \
  -out device-001.csr \
  -subj "/CN=device-001"

openssl x509 -req \
  -in device-001.csr \
  -CA intermediate-ca.pem \
  -CAkey intermediate-ca.key \
  -out device-001.pem \
  -days 365 -sha256

# Repeat for device-002, device-003, etc.
```

### Step 4: Deploy Certificates to Devices
Then we copy certs + keys onto devices and plan for secure storage and rotation.

- Copy device certificate + private key to each device
- Secure storage required (TPM, secure element, etc.)
- Manual certificate rotation when certificates expire

### Problems with Traditional Approach

❌ **Complex:** Need to run your own CA infrastructure  
❌ **Manual:** Generate and deploy certificates for each device  
❌ **Rotation:** Manual process to renew expiring certificates  
❌ **Security Risk:** Private keys generated on host machine, not device  
❌ **Operational Overhead:** Certificate lifecycle management  

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

```csharp
// Step 1: Device generates private key (RSA or ECC)
using var rsa = RSA.Create(2048);

// Step 2: Build CSR that names the device
var request = new CertificateRequest(
  $"CN={registrationId}",
  rsa,
  HashAlgorithmName.SHA256,
  RSASignaturePadding.Pkcs1
);

// Step 3: Export CSR (DER -> Base64) to send to DPS
byte[] csrDer = request.CreateSigningRequest();
string csrBase64 = Convert.ToBase64String(csrDer);

// Step 4: Submit CSR during registration
var registrationPayload = new {
  registrationId = registrationId,
  csr = csrBase64
};

// Step 5-8: DPS validates, ADR signs, returns chain

// Step 9: Combine issued cert with the private key
var certificate = new X509Certificate2(issuedCertBytes);
var certWithKey = certificate.CopyWithPrivateKey(rsa);
```

### Advantages of CSR Approach

✅ **No CA Management:** Microsoft manages the Certificate Authority  
✅ **Private Key Security:** Keys generated on device, never transmitted  
✅ **Automated:** Certificates issued during provisioning  
✅ **Scalable:** Works for millions of devices  
✅ **Lifecycle Management:** ADR credential policies handle renewal  
✅ **Zero-Touch:** Device provisions itself on first boot  

## Comparing Both Approaches

| Feature | Traditional X.509 | CSR-Based (New) |
|---------|------------------|-----------------|
| **CA Management** | Self-hosted | Microsoft-managed |
| **Certificate Generation** | Pre-provisioned | Just-in-time |
| **Private Key Location** | Generated on host | Generated on device |
| **Deployment Complexity** | High (certs per device) | Low (config only) |
| **Rotation** | Manual | Automated |
| **Proof of Possession** | Required | Not required |
| **Device Provisioning** | Pre-configured | Zero-touch |
| **Best For** | Existing PKI infrastructure | New deployments |

## Dual Attestation Pattern

One powerful pattern: **authenticate with symmetric key, receive X.509 certificate**.

### Phase 1: Provisioning (Symmetric Key)

```csharp
// Simple shared secret authentication
var deviceKey = DeriveDeviceKey(enrollmentGroupKey, registrationId);
var sasToken = GenerateSasToken(deviceKey, idScope, registrationId);

// Connect to DPS with SAS token
// Submit CSR
// Receive X.509 certificate
```

### Phase 2: Operation (X.509)

```csharp
// Use the issued X.509 certificate for IoT Hub
var deviceClient = DeviceClient.Create(
    assignedHub,
    new DeviceAuthenticationWithX509Certificate(deviceId, certificate)
);
```

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

```csharp
// Response from DPS contains array of certificates
var issuedCertificateChain = response.registrationState.issuedCertificateChain;

// [0] = Device certificate
// [1] = Intermediate CA
// [2] = Root CA (optional, usually in system trust store)

// Decode and combine
var deviceCertBytes = Convert.FromBase64String(issuedCertificateChain[0]);
var deviceCert = new X509Certificate2(deviceCertBytes);

// Combine with private key
var certWithKey = deviceCert.CopyWithPrivateKey(rsaKey);

// Export to PFX for persistence
byte[] pfx = certWithKey.Export(X509ContentType.Pfx, password);
File.WriteAllBytes("device-cert.pfx", pfx);
```

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

**Device code for renewal:**

```csharp
// Check certificate expiration
if (certificate.NotAfter.AddDays(-7) <= DateTime.UtcNow)
{
    // Within renewal window - request new certificate
    await RenewCertificateAsync();
}
```

## CSR Generation Deep Dive

### RSA vs ECC

**RSA (Rivest-Shamir-Adleman):**
- Traditional, widely supported
- 2048-bit or 4096-bit keys
- Larger key size = slower operations

```csharp
using var rsa = RSA.Create(2048);
var request = new CertificateRequest(
    $"CN={deviceId}",
    rsa,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1
);
```

**ECC (Elliptic Curve Cryptography):**
- Modern, more efficient
- 256-bit provides equivalent security to RSA 3072-bit
- Smaller keys, faster operations, lower power consumption

```csharp
using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var request = new CertificateRequest(
    $"CN={deviceId}",
    ecdsa,
    HashAlgorithmName.SHA256
);
```

**For IoT devices:** ECC is recommended (smaller, faster, less power).

### CSR Format

CSR must be in **Base64-encoded DER format** (not PEM):

```csharp
// Generate CSR
byte[] csrDer = request.CreateSigningRequest();

// Convert to Base64 (no PEM headers/footers)
string csrBase64 = Convert.ToBase64String(csrDer);

// This is what gets sent in the registration payload:
// {
//   "registrationId": "device-001",
//   "csr": "MIICXTCCAUUCAQAwGDEWMBQGA1UEAxMNZGV2aWNlLTAwMTCCAS..."
// }
```

## Security Best Practices

### Private Key Storage

✅ **Do:**
- Generate keys on device (never transmit)
- Use hardware security modules (TPM, secure element)
- Encrypt at rest if storing in filesystem
- Use strong file permissions (chmod 600)

❌ **Don't:**
- Generate keys on a server and transfer to device
- Store unencrypted in filesystem
- Hardcode in source code
- Log or transmit private keys

### Certificate Validation

Always validate certificates when connecting:

```csharp
var certificate = new X509Certificate2("device-cert.pfx", password);

// Validate expiration
if (certificate.NotAfter <= DateTime.UtcNow)
    throw new Exception("Certificate expired");

// Validate subject
if (!certificate.Subject.Contains(deviceId))
    throw new Exception("Certificate subject mismatch");

// Validate chain (IoT Hub should trust issuer)
var chain = new X509Chain();
if (!chain.Build(certificate))
    throw new Exception("Certificate chain validation failed");
```

## Testing Certificate Workflows

### Test CSR Generation Locally

```csharp
// Generate CSR
using var rsa = RSA.Create(2048);
var request = new CertificateRequest(
    "CN=test-device",
    rsa,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1
);

byte[] csrDer = request.CreateSigningRequest();
string csrPem = PemEncoding.Write("CERTIFICATE REQUEST", csrDer);

Console.WriteLine(csrPem);
// -----BEGIN CERTIFICATE REQUEST-----
// MIICXTCCAUUCAQAwGDEWMBQGA1UEAxMNdGVzdC1kZXZpY2UwggEiMA0GCSqGSIb3
// ...
// -----END CERTIFICATE REQUEST-----

// Verify with OpenSSL
// Save to file and run: openssl req -text -noout -in test.csr
```

## Next Steps

Now that you understand certificate workflows, we'll configure enrollment groups to use these certificates:
- Creating symmetric key enrollment groups
- Creating X.509 enrollment groups  
- Linking credential policies for CSR issuance
- Testing enrollment configurations

---

[Next: Configuring Enrollment Groups →](04-configuring-enrollment-groups.md)
