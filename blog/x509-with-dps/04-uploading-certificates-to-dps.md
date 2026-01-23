# Uploading Certificates to DPS

[← Previous: Creating X.509 Certificates](03-creating-x509-certificates.md) | [Next: Verifying X.509 Certificates →](05-verifying-x509-certificates.md)

---

## Uploading to the Cloud

Now that we have our certificates created locally, it's time to upload them to DPS. But not all of them! We're about to see which ones go where and why.

## Which Certificates to Upload?

Before we start uploading, let's be clear about what goes where:

| Certificate | Upload to DPS? | Purpose |
|-------------|----------------|---------|
| Root CA | ✅ YES | Trust anchor, must be verified |
| Intermediate CA | ✅ YES | Used in enrollment groups, must be verified |
| Device Certificate | ❌ NO | Devices present this, signed by intermediate |

**Key Point:** You NEVER upload device certificates to DPS. Instead, you upload and verify the CA certificates that signed them.

## Why This Works

DPS uses a trust chain model:
1. You upload and verify CA certificates in DPS
2. Devices present certificates signed by those CAs
3. DPS validates the signature chain
4. If valid, device is trusted and registered

## Prerequisites

- DPS instance created (from previous section)
- Root and intermediate CA certificates generated (from section 03)
- Azure CLI with IoT extension installed
- Certificates stored in `certs/root/` and `certs/intermediate/` directories

**Set your variables:**

```powershell
# Set variables (use your actual names)
$dpsName = "my-dps-x509"
$resourceGroup = "iot-x509-demo-rg"
```

**Verify everything is ready:**

```powershell
# Check DPS exists
az iot dps show --name $dpsName --resource-group $resourceGroup

# Check certificates exist locally
Get-ChildItem certs/root/root.pem
Get-ChildItem certs/intermediate/intermediate.pem
```

## Step 1: Upload Root CA Certificate

First, we'll upload the root CA certificate. This is the trust anchor for your entire certificate hierarchy.

```powershell
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --path "certs/root/root.pem"
```

**What this command does:**
- `create`: Adds a new certificate to DPS
- `--certificate-name`: Friendly name for the certificate in DPS (not the CN, just for organization)
- `--path`: Full or relative path to your root CA .pem file

**Expected output:**
```json
{
  "etag": "AAAAAAFPTRo=",
  "id": "/subscriptions/.../certificates/my-root-ca",
  "name": "my-root-ca",
  "properties": {
    "certificate": "-----BEGIN CERTIFICATE-----...",
    "created": "2026-01-21T10:00:00Z",
    "isVerified": false,
    "subject": "CN=My-Root-CA",
    "thumbprint": "ABCDEF0123456789...",
    "updated": "2026-01-21T10:00:00Z"
  }
}
```

**Look for:**
- ✅ `"name": "my-root-ca"` - Confirms certificate name
- ✅ `"subject": "CN=My-Root-CA"` - Confirms this is your root CA
- ⚠️ `"isVerified": false` - Expected! We'll verify in the next section
- 📝 `"etag": "..."` - Save this for the verification step (or we can retrieve it later)

## Step 2: Upload Intermediate CA Certificate

Next, upload the intermediate CA certificate. DPS will use this in the enrollment group to validate device certificates.

```powershell
az iot dps certificate create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-intermediate-ca" `
  --path "certs/intermediate/intermediate.pem"
```

**Look for:**
- ✅ `"name": "my-intermediate-ca"` - Confirms name
- ✅ `"subject": "CN=My-Intermediate-CA"` - Confirms intermediate CA
- ⚠️ `"isVerified": false` - Expected, we'll verify next

## Step 3: Verify Upload via Azure Portal

Let's confirm both uploads in the Azure Portal. This visual check ensures the certificates are in DPS before we proceed to verification.

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Go to your DPS instance
3. Click **Certificates** in the left menu

You should see both certificates listed:

| Name | Subject | Thumbprint | Status | Created |
|------|---------|------------|--------|---------|
| my-root-ca | CN=My-Root-CA | ABCD... | ⚠️ Unverified | Jan 21, 2026 |
| my-intermediate-ca | CN=My-Intermediate-CA | EF01... | ⚠️ Unverified | Jan 21, 2026 |

## View Certificate Details

```powershell
# View root CA details
az iot dps certificate show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --query "{name:name, subject:properties.subject, isVerified:properties.isVerified}" `
  -o table
```

**Output:**
```
Name          Subject          IsVerified
------------  ---------------  -----------
my-root-ca    CN=My-Root-CA    False
```

## List All Certificates

```powershell
az iot dps certificate list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --query "value[].{Name:name, Subject:properties.subject, Verified:properties.isVerified}" `
  -o table
```

**Output:**
```
Name                 Subject                    Verified
-------------------  -------------------------  ---------
my-root-ca           CN=My-Root-CA              False
my-intermediate-ca   CN=My-Intermediate-CA      False
```

## Common Errors and Solutions

### Error: "Certificate already exists"

```
Error: Certificate with name 'my-root-ca' already exists
```

**Solution:** Either delete the existing certificate or use a different name:

```powershell
# Delete existing certificate
az iot dps certificate delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name "my-root-ca" `
  --etag "AAAAAAFPTRo="

# Or use a different name
--certificate-name "my-root-ca-v2"
```

### Error: "Invalid certificate format"

```
Error: The certificate is not in valid PEM format
```

**Solution:** Verify your .pem file:
- Should start with `-----BEGIN CERTIFICATE-----`
- Should end with `-----END CERTIFICATE-----`
- No extra whitespace or characters

```powershell
# View the file
Get-Content certs/root/root.pem

# Should look like:
# -----BEGIN CERTIFICATE-----
# MIIDXTCCAkWgAwIBAgIJAKJ3mE...
# ...
# -----END CERTIFICATE-----
```

### Error: "File not found"

```
Error: Unable to open file 'certs/root/root.pem'
```

**Solution:** Check your path:
```powershell
# Use absolute path
--path "C:\repos\skittlesorter\certs\root\root.pem"

# Or navigate to the directory first
cd C:\repos\skittlesorter
--path "certs/root/root.pem"
```

## What About Device Certificates?

**Do NOT upload device certificates to DPS!**

Here's why:
- ❌ Doesn't scale (imagine 10,000 devices)
- ❌ Defeats the purpose of certificate chains
- ❌ DPS doesn't need them

Instead:
- ✅ Upload and verify CA certificates (root and intermediate)
- ✅ Devices present their certificates during provisioning
- ✅ DPS validates the signature chain automatically

## Understanding the Status

After uploading, certificates have an **Unverified** status. This is expected and important:

### Why Unverified?

DPS requires proof that you own the private key for the CA certificate. Anyone can upload a public certificate, but only the owner of the private key can sign verification certificates.

### What Verification Does

Verification proves:
1. ✅ You possess the CA private key
2. ✅ You can sign certificates with this CA
3. ✅ Devices presenting certificates signed by this CA should be trusted

## Certificate Properties to Note

When you view a certificate in DPS, pay attention to:

**Subject:** The CN (Common Name) from the certificate
- Root: `CN=My-Root-CA`
- Intermediate: `CN=My-Intermediate-CA`

**Thumbprint:** SHA-1 fingerprint (unique identifier)
- Used for verification commands
- Useful for debugging

**Created/Updated:** Timestamps
- Track when certificates were uploaded or modified

**isVerified:** Boolean status
- `false` = uploaded but not verified
- `true` = verified and trusted (what we want)

## Next Steps

Now that we've uploaded our CA certificates, we need to verify them through the proof-of-possession process. This is a critical security step that proves we own the private keys.

In the next section, we'll:
1. Generate verification codes from DPS
2. Create verification certificates
3. Complete the verification process
4. See our certificates marked as "Verified" ✅

---

[← Previous: Creating X.509 Certificates](03-creating-x509-certificates.md) | [Next: Verifying X.509 Certificates →](05-verifying-x509-certificates.md)
