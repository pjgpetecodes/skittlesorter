# Creating X.509 Certificates with OpenSSL

[← Previous: Creating DPS and IoT Hub](02-creating-dps-and-iot-hub.md) | [Next: Uploading Certificates to DPS →](04-uploading-certificates-to-dps.md)

---

## Certificates: The Security Foundation

Before we run any OpenSSL commands, let's talk about why we use certificates and why we use **three** of them instead of just one. This architecture is industry standard and makes your IoT deployment more secure and easier to manage.

## Why a Certificate Hierarchy?

Before we jump into commands, let's understand why we need THREE certificates instead of just one.

### The Problem with a Single Certificate

If you create just one certificate for your device:
- ❌ If compromised, you must replace it everywhere
- ❌ Hard to scale to many devices
- ❌ No way to revoke subset of devices
- ❌ Root key is constantly used (higher risk)

### The Solution: 3-Tier PKI Hierarchy

```
┌────────────────┐
│   Root CA      │  Trust anchor, kept offline
│  (10 years)    │  Verified once in DPS
└───────┬────────┘
        │ signs
        ▼
┌────────────────┐
│ Intermediate   │  Day-to-day signing
│    CA          │  Can be rotated
│  (5 years)     │  Used in enrollment group
└───────┬────────┘
        │ signs
        ▼
┌────────────────┐
│ Device Cert    │  Identifies this device
│  (1 year)      │  Easy to renew
└────────────────┘
```

### Why This Approach?

**Root CA:**
- Long-lived (10 years)
- Private key stays in secure storage (HSM or offline)
- Only used to sign intermediate CAs
- If compromised, entire PKI must be rebuilt (rare operation)

**Intermediate CA:**
- Medium-lived (5 years)
- Used for day-to-day certificate signing
- Can be revoked and replaced without touching root
- Uploaded and verified in DPS

**Device Certificate:**
- Short-lived (1 year)
- Easy to renew before expiration
- If compromised, only affects one device
- Signed by intermediate (not root)

## Prerequisites

### Install OpenSSL

**Windows:**

If you already have Git installed on your local machine, then it's recommended you use that. We can include it in the path for any openssl commands by running the following:

```powershell
$env:Path = "C:\Program Files\OpenSSL-Win64\bin;$env:Path"
```

If you don't have Git installed, you can also use WinGet:

```powershell
winget install ShiningLight.OpenSSL.Full
```

Alternatively, depending on your requirements, the following methods will work too:

- Download from [Win32OpenSSL](https://slproweb.com/products/Win32OpenSSL.html)
- Or use Git Bash (includes OpenSSL)
- Or use WSL (Windows Subsystem for Linux)

**macOS:**
```bash
brew install openssl
```

**Linux:**
```bash
sudo apt-get install openssl  # Debian/Ubuntu
sudo yum install openssl      # RHEL/CentOS
```

**Verify installation:**
```powershell
openssl version
# Expected: OpenSSL 3.0.x or higher
```

## Step 1: Create Directory Structure

First we'll setup the folders to store our certs.

```powershell
# Create organized directory structure
New-Item -ItemType Directory -Force -Path certs/root, certs/intermediate, certs/device, certs/issued

# Verify structure
Get-ChildItem certs/ -Directory
# Output:
# Directory: certs
# Mode    Name
# ----    ----
# d----   device
# d----   intermediate
# d----   issued
# d----   root
```

## Step 2: Generate Root CA (Self-Signed)

Next we'll generate the Root CA Certificate what all our other certificates will be based on. The root CA is the trust anchor for our entire certificate chain.

```powershell
openssl req -x509 -new -nodes `
  -newkey rsa:4096 `
  -keyout certs/root/root.key `
  -out certs/root/root.pem `
  -days 3650 `
  -sha256 `
  -subj "/CN=My-Root-CA" `
  -addext "basicConstraints=critical,CA:true,pathlen:1" `
  -addext "keyUsage=critical,keyCertSign,cRLSign" `
  -addext "subjectKeyIdentifier=hash" `
  -addext "authorityKeyIdentifier=keyid:always"
```

**What this command does:**
- `-x509`: Create self-signed certificate
- `-newkey rsa:4096`: Generate 4096-bit RSA key pair
- `-days 3650`: Valid for 10 years
- `-subj "/CN=My-Root-CA"`: Certificate common name
- `pathlen:1`: Can sign 1 level down (intermediate)
- `keyCertSign`: Can sign other certificates

**Verify that the Root CA was created correctly:**
```powershell
openssl x509 -in certs/root/root.pem -text -noout
```

Look for:

✅ Issuer: CN = My-Root-CA
✅ Subject: CN = My-Root-CA (self-signed)
✅ CA:TRUE, pathlen:1

## Step 3: Generate Intermediate CA CSR

Now we create an intermediate CA that will be signed by the Root and used to create our Leaf, or Device Certificate.

```powershell
# Generate intermediate CA private key and CSR
openssl req -new -nodes `
  -newkey rsa:4096 `
  -keyout certs/intermediate/intermediate.key `
  -out certs/intermediate/intermediate.csr `
  -subj "/CN=My-Intermediate-CA"
```

**Create an extension file for the Intermediate Certificate:**

This next PowerShell command writes a block of OpenSSL extension settings into intermediate-ext.cnf. Those settings define the certificate as an intermediate CA with the correct key-usage and constraints.

```powershell
@"
[v3_intermediate]
basicConstraints = critical,CA:true,pathlen:0
keyUsage = critical,keyCertSign,cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path certs/intermediate/intermediate-ext.cnf -Encoding ASCII
```

**Sign the Intermediate Certificate with the Root Certificate:**

Now we use our Root CA to sign the Intermediate CSR, producing a fully trusted Intermediate Certificate we can use to create Leaf Certificates.

```powershell
openssl x509 -req `
  -in certs/intermediate/intermediate.csr `
  -CA certs/root/root.pem `
  -CAkey certs/root/root.key `
  -CAcreateserial `
  -out certs/intermediate/intermediate.pem `
  -days 1825 `
  -sha256 `
  -extfile certs/intermediate/intermediate-ext.cnf `
  -extensions v3_intermediate
```

**What's different:**
- `pathlen:0`: Cannot sign more CAs (end of chain)
- `days 1825`: Valid for 5 years
- Signed by root (not self-signed)

**Verify that the Intermediate Certificate was created successfully:**

We can now verify that the Intermediate Certificate was correctly signed by the Root, then inspect its full decoded contents.

```powershell
# Verify signature
openssl verify -CAfile certs/root/root.pem certs/intermediate/intermediate.pem
# Expected: certs/intermediate/intermediate.pem: OK

# View certificate
openssl x509 -in certs/intermediate/intermediate.pem -text -noout
```

Look for:

✅ Issuer: CN = My-Root-CA (signed by root)
✅ Subject: CN = My-Intermediate-CA
✅ CA:TRUE, pathlen:0

## Step 4: Generate Device Certificate

Now we create a Device Certificate signed by the Intermediate CA.

```powershell
# Generate device private key and CSR
openssl req -new -nodes `
  -newkey rsa:2048 `
  -keyout certs/device/device.key `
  -out certs/device/device.csr `
  -subj "/CN=my-device-001"
```

⚠️ **Important:** The CN (Common Name) `my-device-001` must match your device's **Registration ID** in code that we'll set later.

**Create an extension file for our Device Certificate:**

In the same way as the previous stages, we create an extension file to specify the usage and identity fields for our Device Certificate.

```powershell
@"
[v3_req]
basicConstraints = CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = clientAuth
subjectAltName = DNS:my-device-001
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path certs/device/device-ext.cnf -Encoding ASCII
```

**Sign Device Certificate with the Intermediate Certificate:**

And now we can sign our Device Certificate with the Intermediate Certificate to add it to the Chain, producing a fully trusted Device Certificate.

```powershell
openssl x509 -req `
  -in certs/device/device.csr `
  -CA certs/intermediate/intermediate.pem `
  -CAkey certs/intermediate/intermediate.key `
  -CAcreateserial `
  -out certs/device/device.pem `
  -days 365 `
  -sha256 `
  -extfile certs/device/device-ext.cnf `
  -extensions v3_req
```

**What's different:**
- `CA:FALSE`: This is NOT a CA certificate
- `clientAuth`: Used for TLS client authentication
- `days 365`: Valid for 1 year (easier to rotate)
- `-newkey rsa:2048`: Smaller key for devices (faster)

**Verify device certificate:**

Let's verify that our Device Certificate was created successfully.

```powershell
# Verify signature
openssl verify `
  -CAfile certs/root/root.pem `
  -untrusted certs/intermediate/intermediate.pem `
  certs/device/device.pem
# Expected: certs/device/device.pem: OK

# View certificate
openssl x509 -in certs/device/device.pem -text -noout
```

Look for:

✅ Issuer: CN = My-Intermediate-CA
✅ Subject: CN = my-device-001
✅ CA:FALSE
✅ X509v3 Extended Key Usage: TLS Web Client Authentication

## Step 5: Create Certificate Chains

Devices need to present the full certificate chain during TLS handshake.

```powershell
# Chain without device cert (intermediate + root)
Get-Content certs/intermediate/intermediate.pem, certs/root/root.pem | Set-Content -Path certs/device/chain.pem -Encoding ASCII

# Full chain with device cert (device + intermediate + root)
Get-Content certs/device/device.pem, certs/intermediate/intermediate.pem, certs/root/root.pem | Set-Content -Path certs/device/device-full-chain.pem -Encoding ASCII
```

**When to use which:**
- `device.pem`: Device certificate only (sometimes needed)
- `chain.pem`: Intermediate + root (for DPS TLS)
- `device-full-chain.pem`: Complete chain (most common)

## Step 6: View Certificate Details

Finally we can run a few local sanity checks on the device certificate, such as confirming its fingerprint, validity dates, and ensuring the chain is cryptographically sound.

This isn't the same as verifying a certificate in DPS – we'll come to that later. But it does verify that the certificate and its chain are structurally correct before you hand it over to DPS for the real enrollment checks.

```powershell
# Get certificate thumbprint (fingerprint)
openssl x509 -in certs/device/device.pem -fingerprint -noout
# SHA256 Fingerprint=AB:CD:EF:...

# Check expiration dates
openssl x509 -in certs/device/device.pem -noout -dates
# notBefore=Jan 21 10:00:00 2026 GMT
# notAfter=Jan 21 10:00:00 2027 GMT

# Verify the entire chain
openssl verify -CAfile certs/root/root.pem `
  -untrusted certs/intermediate/intermediate.pem `
  certs/device/device.pem
# certs/device/device.pem: OK
```

## Understanding PEM Format

All our certificates are in PEM (Privacy-Enhanced Mail) format:

```
-----BEGIN CERTIFICATE-----
MIIDXTCCAkWgAwIBAgIJAKJ3mE...
(Base64 encoded certificate data)
...
-----END CERTIFICATE-----
```

**Why PEM?**

✅ Text format (easy to view, copy, paste)
✅ Standard format for Azure and most systems
✅ Can concatenate multiple certs in one file

## File Summary

After completing this section, you should have the following files and folders:

```
certs/
├── root/
│   ├── root.key              # Root private key (KEEP SECURE!)
│   ├── root.pem              # Root certificate (upload to DPS)
│   └── root.pem.srl          # Serial number file
├── intermediate/
│   ├── intermediate.key      # Intermediate private key (KEEP SECURE!)
│   ├── intermediate.pem      # Intermediate certificate (upload to DPS)
│   ├── intermediate.csr      # CSR (can delete)
│   ├── intermediate-ext.cnf  # Extension file (can delete)
│   └── intermediate.pem.srl  # Serial number file
└── device/
    ├── device.key            # Device private key (deploy to device)
    ├── device.pem            # Device certificate (deploy to device)
    ├── device.csr            # CSR (can delete)
    ├── device-ext.cnf        # Extension file (can delete)
    ├── chain.pem             # Intermediate + root chain
    └── device-full-chain.pem # Full chain (deploy to device)
```

## Security Best Practices

🔒 **Root CA private key** (`root.key`):
- Most sensitive file
- Should be stored offline or in HSM
- Never deploy to devices
- Only used for signing intermediate CAs

🔒 **Intermediate CA private key** (`intermediate.key`):
- Used for day-to-day signing
- Store securely (not on public servers)
- Never deploy to devices

🔒 **Device private key** (`device.key`):
- Deploy to device securely
- Unique per device
- Never share between devices

⚠️ **Never commit private keys to source control!**

If you're using git to source control perhaps a parent folder, then you must make sure not to check your certificates in to your repo, otherwise your certificates and any devices could be compromised.

Add to `.gitignore`:
```
certs/**/*.key
certs/**/*.csr
```

Or create/update via PowerShell:
```powershell
Add-Content .gitignore "`ncerts/**/*.key`ncerts/**/*.csr"
```

## Important Notes on Production

This guide creates certificates manually for development and testing. **In production scenarios:**

- You won't manually create certificates for each device
- Instead, devices typically generate their own certificate at first boot, or certificates are created during manufacturing/enrollment
- Alternatively, automated batch generation processes are used
- The architecture remains the same (3-tier PKI), but the generation is automated at scale

Additionally, in production you should also upload these CA certificates to IoT Hub (in addition to DPS) for defense-in-depth validation of device certificates.

## Next Steps

Now that we have our certificate hierarchy, we'll upload the CA certificates to DPS and verify them.

---

[← Previous: Creating DPS and IoT Hub](02-creating-dps-and-iot-hub.md) | [Next: Uploading Certificates to DPS →](04-uploading-certificates-to-dps.md)
