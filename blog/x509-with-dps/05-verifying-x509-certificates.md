# Verifying X.509 Certificates (Proof of Possession)

[← Previous: Uploading Certificates to DPS](04-uploading-certificates-to-dps.md) | [Next: Creating Enrollment Groups →](06-creating-enrollment-groups.md)

---

## Proof of Possession: Proving You Own Your Certificates

Your certificates are now uploaded to DPS, but they're marked "Unverified." Before devices can use them for provisioning, you need to prove you control the private keys. This section covers the proof-of-possession mechanism Azure implements to verify certificate ownership.

## Why Verification is Required

After uploading certificates to DPS, they remain in **Unverified** status. This is a critical security measure.

### The Problem

Anyone can download a public certificate and upload it to DPS. For example:
- Public root CAs from certificate authorities
- Stolen or leaked certificates
- Certificates from other organizations

**Without verification, you could claim to own certificates you don't control.**

### The Solution: Proof of Possession

DPS implements "proof of possession" - you must prove you have the private key by:
1. DPS generates a random verification code
2. You create a certificate with that code as the CN (Common Name)
3. You sign it with your CA private key
4. You upload the signed verification certificate
5. DPS validates the signature

**If the signature is valid, DPS knows you own the private key.**

## The Verification Process Flow

```
┌─────────────┐
│     DPS     │
└──────┬──────┘
       │
       │ 1. Generate verification code: "ABC123XYZ"
       │
       ▼
┌─────────────┐
│     You     │
└──────┬──────┘
       │
       │ 2. Create certificate with CN=ABC123XYZ
       │ 3. Sign it with CA private key
       │
       ▼
┌─────────────┐
│     DPS     │
└──────┬──────┘
       │
       │ 4. Validate signature
       │ 5. Mark CA as "Verified" ✅
       │
       ▼
```

## Prerequisites

- Root and intermediate CA certificates uploaded to DPS (unverified)
- Access to CA private keys
- OpenSSL installed

```powershell
# Set variables
$dpsName = "my-dps-x509"
$resourceGroup = "iot-x509-demo-rg"
```

## Part 1: Verify Root CA

### Step 1: Generate Verification Code

First, we ask DPS to generate a random verification code. We'll use this code as the CN in a test certificate that we sign with our private key.

```powershell
# Get current etag (needed for updates)
$rootCert = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  -o json | ConvertFrom-Json

$etag = $rootCert.etag

# Generate verification code
az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --etag $etag
```

**What this does:**
- Gets the current certificate from DPS (including its etag)
- Etag is a version identifier - needed to prevent conflicts during updates
- Generates a random verification code that we'll use as a certificate CN

**Expected output:**
```json
{
  "etag": "AAAAAAFPTRp=",
  "properties": {
    "verificationCode": "A1B2C3D4E5F6G7H8"
  }
}
```

**Look for:**
- ✅ `"verificationCode": "A1B2C3D4E5F6G7H8"` - This is your unique verification code

### Step 2: Retrieve the Verification Code

Next we extract the verification code and save it to a variable for the upcoming certificate creation.

```powershell
$verificationCode = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --query "properties.verificationCode" `
  -o tsv

Write-Host "Verification Code: $verificationCode" -ForegroundColor Green
```

**Output example:**
```
Verification Code: A1B2C3D4E5F6G7H8
```

**Next:** We'll use this exact code as the CN in our verification certificate.

### Step 3: Create Verification Certificate

In this step we create a certificate with the verification code as the CN and sign it with our root CA private key to prove ownership.

```powershell
# Generate private key for verification cert
openssl genrsa -out certs/root/verification.key 2048

# Create CSR with verification code as CN
openssl req -new `
  -key certs/root/verification.key `
  -out certs/root/verification.csr `
  -subj "/CN=$verificationCode"

# Sign with root CA private key
openssl x509 -req `
  -in certs/root/verification.csr `
  -CA certs/root/root.pem `
  -CAkey certs/root/root.key `
  -CAcreateserial `
  -out certs/root/verification.pem `
  -days 1 `
  -sha256
```

**What this does:**
1. Generates a temporary private key for the verification cert
2. Creates a CSR with the verification code as CN (must match exactly)
3. Signs it with your root CA private key (proves you have the key)

**Important:**
- CN must EXACTLY match your verification code (case-sensitive)
- Only valid for 1 day (we only need it for this proof)
- Signed by root CA private key (the proof that you own it)
- No special extensions needed

**Verify the certificate:**
```powershell
# Check the CN
openssl x509 -in certs/root/verification.pem -noout -subject
# Subject: CN = A1B2C3D4E5F6G7H8

# Verify signature
openssl verify -CAfile certs/root/root.pem certs/root/verification.pem
# verification.pem: OK
```

### Step 4: Upload Verification Certificate

Now we need to submit the signed verification certificate back to DPS to complete the proof-of-possession process.

```powershell
# Get fresh etag
$etag = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --query "etag" `
  -o tsv

# Submit verification certificate
az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --path "certs/root/verification.pem" `
  --etag $etag
```

**Expected output:**
```json
{
  "etag": "AAAAAAFPTRq=",
  "properties": {
    "isVerified": true,
    "subject": "CN=My-Root-CA",
    "thumbprint": "ABCDEF0123456789..."
  }
}
```

**✅ Success!** Notice `"isVerified": true`

### Step 5: Confirm Verification

Finally we check that the root CA is now marked as verified in DPS.

```powershell
az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --query "{Name:name, Verified:properties.isVerified}" `
  -o table
```

**Output:**
```
Name          Verified
------------  ---------
my-root-ca    True
```

## Part 2: Verify Intermediate CA

Now repeat the process for the intermediate CA.

### Step 1: Generate Verification Code

Now we need to repeat the verification process for the intermediate CA by requesting its unique verification code from DPS.

```powershell
$intermediateCert = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  -o json | ConvertFrom-Json

az iot dps certificate generate-verification-code `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  --etag $intermediateCert.etag

$verificationCode = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  --query "properties.verificationCode" `
  -o tsv

Write-Host "Intermediate Verification Code: $verificationCode" -ForegroundColor Green
```

### Step 2: Create Verification Certificate

In this step we create a certificate with the verification code as CN, but this time we sign it with the intermediate CA private key.

```powershell
# Generate private key
openssl genrsa -out certs/intermediate/verification-intermediate.key 2048

# Create CSR with verification code
openssl req -new `
  -key certs/intermediate/verification-intermediate.key `
  -out certs/intermediate/verification-intermediate.csr `
  -subj "/CN=$verificationCode"

# Sign with intermediate CA private key
openssl x509 -req `
  -in certs/intermediate/verification-intermediate.csr `
  -CA certs/intermediate/intermediate.pem `
  -CAkey certs/intermediate/intermediate.key `
  -CAcreateserial `
  -out certs/intermediate/verification-intermediate.pem `
  -days 1 `
  -sha256
```

### Step 3: Upload Verification Certificate

Next we submit the signed verification certificate to DPS to complete the proof-of-possession for the intermediate CA.

```powershell
$etag = az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  --query "etag" `
  -o tsv

az iot dps certificate verify `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  --path "certs/intermediate/verification-intermediate.pem" `
  --etag $etag
```

### Step 4: Confirm Both Verified

Finally we verify that both the root and intermediate CA certificates now show as verified in DPS.

```powershell
az iot dps certificate list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --query "value[].{Name:name, Subject:properties.subject, Verified:properties.isVerified}" `
  -o table
```

**Expected output:**
```
Name                 Subject                    Verified
-------------------  -------------------------  ---------
my-root-ca           CN=My-Root-CA              True
my-intermediate-ca   CN=My-Intermediate-CA      True
```

**🎉 Both certificates are now verified!**

## Verify in Azure Portal

1. Navigate to your DPS instance
2. Click **Certificates**
3. Both certificates should show:
   - ✅ Green checkmark
   - Status: **Verified**

## What Just Happened?

Let's recap the security model:

1. **You uploaded certificates** - Anyone could do this
2. **DPS generated random codes** - Unique challenges
3. **You created certificates with those codes** - Required CN match
4. **You signed with private keys** - Proves possession
5. **DPS validated signatures** - Confirmed ownership

**Now DPS trusts certificates signed by your CAs** because you proved you own the private keys.

## Common Errors and Solutions

### Error: "Etag mismatch"

```
Error: The ETag provided does not match the current certificate ETag
```

**Solution:** The etag changed between commands (someone else modified it, or you waited too long).

```powershell
# Get fresh etag and retry
$etag = az iot dps certificate show ... --query "etag" -o tsv
```

### Error: "Verification code mismatch"

```
Error: The verification code in the certificate does not match
```

**Solution:** Check the CN in your verification certificate:

```bash
openssl x509 -in verification.pem -noout -subject
# Make sure it EXACTLY matches the verification code
```

### Error: "Certificate signature verification failed"

```
Error: The certificate signature could not be verified
```

**Solution:** You signed with the wrong CA key.

- Root verification cert must be signed with `root.key`
- Intermediate verification cert must be signed with `intermediate.key`

```powershell
# Verify locally first
openssl verify -CAfile certs/root/root.pem certs/root/verification.pem
```

### Error: "Certificate has expired"

```
Error: The verification certificate has expired
```

**Solution:** Generate a new verification certificate. They're only valid for 1 day.

## Cleanup Verification Files

After successful verification, you can delete the verification files:

```powershell
# In certs/root/
Remove-Item certs/root/verification.key, certs/root/verification.csr, certs/root/verification.pem

# In certs/intermediate/
Remove-Item certs/intermediate/verification-intermediate.key, certs/intermediate/verification-intermediate.csr, certs/intermediate/verification-intermediate.pem
```

**Keep the CA certificates and keys** - you'll need them for signing new device certificates.

## Security Implications

Now that certificates are verified:

✅ **DPS will trust devices** presenting certificates signed by your CAs  
✅ **Chain validation happens automatically** - DPS checks signatures  
✅ **No device certificates need upload** - Only CA verification required  
✅ **Scales to millions of devices** - All use same verified CAs  

## Next Steps

With verified CA certificates in DPS, we can now:
1. Create an enrollment group (next section)
2. Provision devices using certificates signed by our CAs
3. Scale to many devices without additional DPS configuration

The hard part is done! Enrollment and provisioning are straightforward from here.

---

[← Previous: Uploading Certificates to DPS](04-uploading-certificates-to-dps.md) | [Next: Creating Enrollment Groups →](06-creating-enrollment-groups.md)
