#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up X.509 attestation for Azure DPS testing
.DESCRIPTION
    Generates bootstrap certificate, creates DPS enrollment group, updates appsettings.json
.PARAMETER DpsName
    Name of your Azure DPS instance
.PARAMETER ResourceGroup
    Azure resource group containing DPS
.PARAMETER RegistrationId
    Enrollment group ID for X.509 attestation (default: skittlesorter)
.PARAMETER EnrollmentGroupId
    Name of the DPS enrollment group to create (defaults to RegistrationId)
.PARAMETER SkipEnrollment
    Skip creating DPS enrollment group (manual setup required)
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$RegistrationId,
    
    [string]$DpsName,
    [string]$ResourceGroup,
    [string]$CredentialPolicy = "default",
    [string]$EnrollmentGroupId,
    [switch]$SkipEnrollment
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath

if (-not $EnrollmentGroupId) { $EnrollmentGroupId = $RegistrationId }
$scriptRoot = Split-Path -Parent $PSCommandPath

Write-Host "`n=== X.509 Attestation Setup for Azure DPS ===" -ForegroundColor Cyan
Write-Host "Registration ID: $RegistrationId`n"

# Step 1: Create certs directory structure
Write-Host "[1/8] Creating certs directory structure..." -ForegroundColor Yellow
$rootDir = Join-Path $scriptRoot "certs/root"
$caDir = Join-Path $scriptRoot "certs/ca"   # intermediate
$deviceDir = Join-Path $scriptRoot "certs/device"
$issuedDir = Join-Path $scriptRoot "certs/issued"
New-Item -ItemType Directory -Force -Path $rootDir | Out-Null
New-Item -ItemType Directory -Force -Path $caDir | Out-Null
New-Item -ItemType Directory -Force -Path $deviceDir | Out-Null
New-Item -ItemType Directory -Force -Path $issuedDir | Out-Null

# Step 2: Generate root CA, intermediate CA, and device certificate using OpenSSL
Write-Host "[2/8] Generating root CA + intermediate CA + device X.509 certificates..." -ForegroundColor Yellow

$rootCertPath = Join-Path $rootDir "root.pem"
$rootKeyPath = Join-Path $rootDir "root.key"
$caCertPath = Join-Path $caDir "ca.pem"
$caKeyPath = Join-Path $caDir "ca.key"
$caCsrPath = Join-Path $caDir "ca.csr"
$chainPath = Join-Path $caDir "chain.pem"
$deviceCertPath = Join-Path $deviceDir "device.pem"
$deviceKeyPath = Join-Path $deviceDir "device.key"
$deviceCsrPath = Join-Path $deviceDir "device.csr"
$deviceFullChainPath = Join-Path $deviceDir "device-full-chain.pem"

# Check if openssl is available
if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    Write-Host "  ⚠ OpenSSL not found. Install: https://slproweb.com/products/Win32OpenSSL.html" -ForegroundColor Red
    exit 1
}

## Root CA (self-signed, pathlen 1)
openssl req -x509 -new -nodes -newkey rsa:4096 -keyout "$rootKeyPath" -out "$rootCertPath" -days 3650 -sha256 `
    -subj "/CN=$RegistrationId-root" `
    -addext "basicConstraints=critical,CA:true,pathlen:1" `
    -addext "keyUsage=critical,keyCertSign,cRLSign" `
    -addext "subjectKeyIdentifier=hash" `
    -addext "authorityKeyIdentifier=keyid:always"

if (-not (Test-Path $rootCertPath)) { Write-Host "  ⚠ Failed to generate root CA certificate" -ForegroundColor Red; exit 1 }

## Intermediate CA CSR (signed by root)
openssl req -new -nodes -newkey rsa:4096 -keyout "$caKeyPath" -out "$caCsrPath" -subj "/CN=$RegistrationId-intermediate"

$interExt = Join-Path $caDir "ca-ext.cnf"
@"
[ v3_intermediate ]
basicConstraints = critical,CA:true,pathlen:0
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path $interExt -Encoding ASCII

openssl x509 -req -in "$caCsrPath" -CA "$rootCertPath" -CAkey "$rootKeyPath" -CAcreateserial -out "$caCertPath" -days 1825 -sha256 `
    -extfile "$interExt" -extensions v3_intermediate

if (-not (Test-Path $caCertPath)) { Write-Host "  ⚠ Failed to generate intermediate certificate" -ForegroundColor Red; exit 1 }

## Device CSR and leaf cert (client auth, signed by intermediate)
openssl req -new -nodes -newkey rsa:2048 -keyout "$deviceKeyPath" -out "$deviceCsrPath" -subj "/CN=$RegistrationId"

$extFile = Join-Path $deviceDir "device-ext.cnf"
@"
[ v3_req ]
basicConstraints = CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = clientAuth
subjectAltName = DNS:$RegistrationId
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path $extFile -Encoding ASCII

openssl x509 -req -in "$deviceCsrPath" -CA "$caCertPath" -CAkey "$caKeyPath" -CAcreateserial -out "$deviceCertPath" -days 365 -sha256 `
    -extfile "$extFile" -extensions v3_req

if (-not (Test-Path $deviceCertPath)) { Write-Host "  ⚠ Failed to generate device certificate" -ForegroundColor Red; exit 1 }

# Build chain file (intermediate + root) for TLS presentation
Get-Content $caCertPath, $rootCertPath | Set-Content -Path $chainPath -Encoding ASCII

# Build full chain (leaf + intermediate + root) for docs/tutorial parity
Get-Content $deviceCertPath, $caCertPath, $rootCertPath | Set-Content -Path $deviceFullChainPath -Encoding ASCII

Write-Host "Root CA Certificate: $rootCertPath" -ForegroundColor Green
Write-Host "Intermediate Certificate: $caCertPath" -ForegroundColor Green
Write-Host "Device Certificate: $deviceCertPath" -ForegroundColor Green
Write-Host "Device Full Chain: $deviceFullChainPath" -ForegroundColor Green
Write-Host "Device Key: $deviceKeyPath" -ForegroundColor Green

# Step 3: Get certificate thumbprints
Write-Host "[3/8] Extracting certificate thumbprints..." -ForegroundColor Yellow

$rootX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($rootCertPath)
$rootThumbprint = $rootX509.Thumbprint

$caX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($caCertPath)
$caThumbprint = $caX509.Thumbprint

$deviceX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($deviceCertPath)
$thumbprint = $deviceX509.Thumbprint
$subject = $deviceX509.Subject

Write-Host "  Root Subject: $($rootX509.Subject)" -ForegroundColor Gray
Write-Host "  Root Thumbprint (verify in DPS): $rootThumbprint" -ForegroundColor Green
Write-Host "  Intermediate Subject: $($caX509.Subject)" -ForegroundColor Gray
Write-Host "  Intermediate Thumbprint (enrollment cert): $caThumbprint" -ForegroundColor Green
Write-Host "  Device Subject: $subject" -ForegroundColor Gray
Write-Host "  Device Thumbprint: $thumbprint" -ForegroundColor Green
Write-Host "  Valid: $($deviceX509.NotBefore) to $($deviceX509.NotAfter)" -ForegroundColor Gray

# Step 4: Upload & verify root CA in DPS
$rootVerified = $false
if ($DpsName -and $ResourceGroup) {
    Write-Host "[4/8] Uploading and verifying root CA in DPS..." -ForegroundColor Yellow
    $rootCertName = "$RegistrationId-root"
    try {
        az iot dps certificate create `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            --path $rootCertPath `
            --output none

        $cert = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            -o json | ConvertFrom-Json
        $etag = if ($cert.properties -and $cert.properties.etag) { $cert.properties.etag } else { $cert.etag }

        $ver = az iot dps certificate generate-verification-code `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            --etag $etag `
            -o json | ConvertFrom-Json
        $code = $ver.properties.verificationCode
        $etag = if ($ver.properties -and $ver.properties.etag) { $ver.properties.etag } else { $ver.etag }

        Write-Host "  Verification Code: $code" -ForegroundColor Green

        $rootDir = Split-Path $rootCertPath
        Push-Location $rootDir
        Remove-Item verification.key, verification.csr, verification.pem, root.pem.srl -ErrorAction SilentlyContinue
        openssl genrsa -out verification.key 2048
        openssl req -new -key verification.key -out verification.csr -subj "/CN=$code"
        openssl x509 -req -in verification.csr -CA root.pem -CAkey $rootKeyPath -CAcreateserial -out verification.pem -days 1 -sha256
        Pop-Location

        $verPem = Join-Path $rootDir "verification.pem"

        az iot dps certificate verify `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            --path $verPem `
            --etag $etag `
            --output none

        $final = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            -o json | ConvertFrom-Json
        $rootVerified = $final.properties.isVerified
        Write-Host "  Root isVerified: $rootVerified" -ForegroundColor Cyan
    } catch {
        Write-Host "  ⚠ Root verification failed: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "[4/8] Skipping root upload/verify (provide -DpsName and -ResourceGroup)" -ForegroundColor Yellow
}

# Step 5: Upload & verify intermediate CA in DPS
$intermediateVerified = $false
if ($DpsName -and $ResourceGroup) {
    Write-Host "[5/8] Uploading and verifying intermediate CA in DPS..." -ForegroundColor Yellow
    $intermediateCertName = "$RegistrationId-intermediate"
    try {
        az iot dps certificate create `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            --path $caCertPath `
            --output none

        $cert = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            -o json | ConvertFrom-Json
        $etag = if ($cert.properties -and $cert.properties.etag) { $cert.properties.etag } else { $cert.etag }

        $ver = az iot dps certificate generate-verification-code `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            --etag $etag `
            -o json | ConvertFrom-Json
        $code = $ver.properties.verificationCode
        $etag = if ($ver.properties -and $ver.properties.etag) { $ver.properties.etag } else { $ver.etag }

        Write-Host "  Verification Code: $code" -ForegroundColor Green

        Push-Location $caDir
        Remove-Item verification-intermediate.key, verification-intermediate.csr, verification-intermediate.pem, ca.pem.srl -ErrorAction SilentlyContinue
        openssl genrsa -out verification-intermediate.key 2048
        openssl req -new -key verification-intermediate.key -out verification-intermediate.csr -subj "/CN=$code"
        openssl x509 -req -in verification-intermediate.csr -CA ca.pem -CAkey ca.key -CAcreateserial -out verification-intermediate.pem -days 1 -sha256
        Pop-Location

        $verPem = Join-Path $caDir "verification-intermediate.pem"

        az iot dps certificate verify `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            --path $verPem `
            --etag $etag `
            --output none

        $final = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            -o json | ConvertFrom-Json
        $intermediateVerified = $final.properties.isVerified
        Write-Host "  Intermediate isVerified: $intermediateVerified" -ForegroundColor Cyan
    } catch {
        Write-Host "  ⚠ Intermediate verification failed: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "[5/8] Skipping intermediate upload/verify (provide -DpsName and -ResourceGroup)" -ForegroundColor Yellow
}

# Step 6: Create DPS enrollment (if Azure CLI available and params provided)
# Step 6: Create DPS enrollment (if Azure CLI available and params provided)
if (-not $SkipEnrollment) {
    if ($DpsName -and $ResourceGroup) {
        Write-Host "[6/8] Creating DPS enrollment group (Intermediate attestation) with credential policy..." -ForegroundColor Yellow
        
        # Check if Azure CLI is available
        if (Get-Command az -ErrorAction SilentlyContinue) {
            try {
                # Use CA reference instead of embedded cert (better for cert rotation)
                az iot dps enrollment-group create `
                    --dps-name $DpsName `
                    --resource-group $ResourceGroup `
                    --enrollment-id $EnrollmentGroupId `
                    --ca-name "$RegistrationId-intermediate" `
                    --credential-policy $CredentialPolicy `
                    --provisioning-status enabled `
                    --output none
                
                Write-Host "  ✓ Enrollment group created with credential policy: $CredentialPolicy" -ForegroundColor Green
                Write-Host "  ✓ CSR-based certificate issuance enabled" -ForegroundColor Green
                Write-Host "  ✓ Using CA reference (intermediate must be verified)" -ForegroundColor Green
            } catch {
                Write-Host "  ⚠ Failed to create enrollment: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "  You may need to create the enrollment manually" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ⚠ Azure CLI not found - skipping enrollment creation" -ForegroundColor Yellow
            Write-Host "  Install: https://aka.ms/installazurecli" -ForegroundColor Gray
            $SkipEnrollment = $true
        }
    } else {
        Write-Host "[6/8] Skipping DPS enrollment (no DPS details provided)" -ForegroundColor Yellow
        Write-Host "  Provide -DpsName and -ResourceGroup to auto-create" -ForegroundColor Gray
        $SkipEnrollment = $true
    }
} else {
    Write-Host "[6/8] Skipping DPS enrollment (manual setup required)" -ForegroundColor Yellow
}

# Step 7: Update appsettings.json
Write-Host "[7/8] Updating appsettings.json..." -ForegroundColor Yellow

$appsettingsPath = Join-Path $scriptRoot "appsettings.json"
if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    
    # Update DPS provisioning settings
    $appsettings.IoTHub.DpsProvisioning.AttestationMethod = "X509"
    $appsettings.IoTHub.DpsProvisioning.RegistrationId = $RegistrationId
    # Device authenticates with its leaf cert; provide full chain (intermediate + root) for TLS
    $appsettings.IoTHub.DpsProvisioning.AttestationCertPath = $deviceCertPath
    $appsettings.IoTHub.DpsProvisioning.AttestationKeyPath = $deviceKeyPath
    $appsettings.IoTHub.DpsProvisioning.AttestationCertChainPath = $chainPath
    $appsettings.IoTHub.DpsProvisioning.EnrollmentGroupKeyBase64 = ""
    $appsettings.IoTHub.DpsProvisioning.EnableDebugLogging = $true
    
    # Save updated settings
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath
    
    Write-Host "  ✓ appsettings.json updated for X.509 attestation" -ForegroundColor Green
} else {
    Write-Host "  ⚠ appsettings.json not found" -ForegroundColor Red
}

# Step 8: Summary and next steps
Write-Host "`n[8/8] Setup Complete!" -ForegroundColor Green
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Root CA Certificate (upload & verify in DPS): $rootCertPath"
Write-Host "Root Thumbprint: $rootThumbprint"
Write-Host "Intermediate Certificate (use in enrollment): $caCertPath"
Write-Host "Intermediate Thumbprint: $caThumbprint"
Write-Host "TLS Chain (intermediate + root): $chainPath"
Write-Host "Device Certificate: $deviceCertPath"
Write-Host "Device Full Chain (leaf+intermediate+root): $deviceFullChainPath"
Write-Host "Device Key: $deviceKeyPath"
Write-Host "Device Thumbprint: $thumbprint"
Write-Host "Registration ID: $RegistrationId"
Write-Host "`nAttestation Method: X509" -ForegroundColor Green

if ($SkipEnrollment) {
    Write-Host "`n=== Manual Steps Required ===" -ForegroundColor Yellow
    Write-Host "Create Enrollment Group with credential policy (intermediate attestation):"
    Write-Host "  az iot dps enrollment-group create"
    Write-Host "    --dps-name <DPS_NAME>"
    Write-Host "    --resource-group <RESOURCE_GROUP>"
    Write-Host "    --enrollment-id $EnrollmentGroupId"
    Write-Host "    --ca-name $RegistrationId-intermediate"
    Write-Host "    --credential-policy $CredentialPolicy"
    Write-Host ""
    Write-Host "  Root (verify in DPS): $rootThumbprint"
    Write-Host "  Intermediate (verify in DPS): $caThumbprint"
    Write-Host "  Leaf: $thumbprint"
} else {
    Write-Host "`n=== Verification Status ===" -ForegroundColor Cyan
    Write-Host "Root CA isVerified: $rootVerified" -ForegroundColor $(if ($rootVerified) { "Green" } else { "Red" })
    Write-Host "Intermediate CA isVerified: $intermediateVerified" -ForegroundColor $(if ($intermediateVerified) { "Green" } else { "Red" })
    if (-not $rootVerified -or -not $intermediateVerified) {
        Write-Host "`n⚠ WARNING: Both root and intermediate must be verified for provisioning to work!" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Test the Flow ===" -ForegroundColor Cyan
Write-Host "Run: dotnet run" -ForegroundColor White
Write-Host "`nExpected: Device authenticates with bootstrap cert, receives new cert via CSR`n"
