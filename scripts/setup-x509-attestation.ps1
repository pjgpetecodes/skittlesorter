#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up X.509 attestation for Azure DPS testing
.DESCRIPTION
    Generates bootstrap certificate and creates DPS enrollment group
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
.PARAMETER SkipVerification
    Skip certificate verification step (uploads certs but doesn't verify them)
.PARAMETER ReuseCa
    Reuse existing local root/intermediate CA files instead of regenerating them
.PARAMETER CaRegistrationId
    CA identity prefix to use for root/intermediate cert names/files (defaults to RegistrationId)
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$RegistrationId,
    [string]$DpsName,
    [string]$ResourceGroup,
    [string]$CredentialPolicy = "default",
    [string]$EnrollmentGroupId,
    [switch]$SkipEnrollment,
    [switch]$SkipVerification,
    [switch]$ReuseCa,
    [string]$CaRegistrationId
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath

if (-not $EnrollmentGroupId) { $EnrollmentGroupId = $RegistrationId }
if (-not $CaRegistrationId) { $CaRegistrationId = $RegistrationId }

Write-Host "`n=== X.509 Attestation Setup for Azure DPS ===" -ForegroundColor Cyan
Write-Host "Registration ID: $RegistrationId"
Write-Host "CA Identity: $CaRegistrationId"
Write-Host "Reuse CA: $($ReuseCa.IsPresent)`n"

# Step 1: Create certs directory structure
Write-Host "[1/7] Creating certs directory structure..." -ForegroundColor Yellow
$rootDir = Join-Path $scriptRoot "certs/root"
$caDir = Join-Path $scriptRoot "certs/ca"   # intermediate
$deviceDir = Join-Path $scriptRoot "certs/device"
$issuedDir = Join-Path $scriptRoot "certs/issued"
New-Item -ItemType Directory -Force -Path $rootDir | Out-Null
New-Item -ItemType Directory -Force -Path $caDir | Out-Null
New-Item -ItemType Directory -Force -Path $deviceDir | Out-Null
New-Item -ItemType Directory -Force -Path $issuedDir | Out-Null

# Step 2: Generate/reuse CA and generate device certificate using OpenSSL
Write-Host "[2/7] Preparing CA and generating device X.509 certificate..." -ForegroundColor Yellow

$safeRegistrationId = ($RegistrationId -replace '[^A-Za-z0-9_.-]', '-')
$safeCaRegistrationId = ($CaRegistrationId -replace '[^A-Za-z0-9_.-]', '-')
$rootCertPath = Join-Path $rootDir "$safeCaRegistrationId-root.pem"
$rootKeyPath = Join-Path $rootDir "$safeCaRegistrationId-root.key"
$caCertPath = Join-Path $caDir "$safeCaRegistrationId-intermediate.pem"
$caKeyPath = Join-Path $caDir "$safeCaRegistrationId-intermediate.key"
$caCsrPath = Join-Path $caDir "$safeCaRegistrationId-intermediate.csr"
$chainPath = Join-Path $caDir "$safeRegistrationId-chain.pem"
$deviceCertPath = Join-Path $deviceDir "$safeRegistrationId-device.pem"
$deviceKeyPath = Join-Path $deviceDir "$safeRegistrationId-device.key"
$deviceCsrPath = Join-Path $deviceDir "$safeRegistrationId-device.csr"
$deviceFullChainPath = Join-Path $deviceDir "$safeRegistrationId-device-full-chain.pem"

# Check if openssl is available
if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    Write-Host "  ⚠ OpenSSL not found. Install: https://slproweb.com/products/Win32OpenSSL.html" -ForegroundColor Red
    exit 1
}

if ($ReuseCa) {
    Write-Host "  Reusing existing root/intermediate CA files..." -ForegroundColor Cyan
    if (-not (Test-Path $rootCertPath) -or -not (Test-Path $rootKeyPath) -or -not (Test-Path $caCertPath) -or -not (Test-Path $caKeyPath)) {
        Write-Host "  ⚠ ReuseCa requested but CA files were not found for CA identity '$CaRegistrationId'" -ForegroundColor Red
        Write-Host "    Expected root cert: $rootCertPath" -ForegroundColor Gray
        Write-Host "    Expected root key:  $rootKeyPath" -ForegroundColor Gray
        Write-Host "    Expected int cert:  $caCertPath" -ForegroundColor Gray
        Write-Host "    Expected int key:   $caKeyPath" -ForegroundColor Gray
        exit 1
    }
} else {
    Write-Host "  Generating new root/intermediate CA files..." -ForegroundColor Cyan
    ## Root CA (self-signed, pathlen 1)
    openssl req -x509 -new -nodes -newkey rsa:4096 -keyout "$rootKeyPath" -out "$rootCertPath" -days 3650 -sha256 `
        -subj "/CN=$CaRegistrationId-root" `
        -addext "basicConstraints=critical,CA:true,pathlen:1" `
        -addext "keyUsage=critical,keyCertSign,cRLSign" `
        -addext "subjectKeyIdentifier=hash" `
        -addext "authorityKeyIdentifier=keyid:always"

    if (-not (Test-Path $rootCertPath)) { Write-Host "  ⚠ Failed to generate root CA certificate" -ForegroundColor Red; exit 1 }

    ## Intermediate CA CSR (signed by root)
    openssl req -new -nodes -newkey rsa:4096 -keyout "$caKeyPath" -out "$caCsrPath" -subj "/CN=$CaRegistrationId-intermediate"

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
}

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
Write-Host "[3/7] Extracting certificate thumbprints..." -ForegroundColor Yellow

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
    if ($SkipVerification) {
        Write-Host "[4/7] Uploading root CA to DPS (verification skipped)..." -ForegroundColor Yellow
    } else {
        Write-Host "[4/7] Uploading and verifying root CA in DPS..." -ForegroundColor Yellow
    }
    $rootCertName = "$CaRegistrationId-root"
    try {
        $existing = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $rootCertName `
            -o json 2>$null

        if ($LASTEXITCODE -eq 0 -and $existing) {
            Write-Host "  ✓ Root CA already exists in DPS: $rootCertName" -ForegroundColor Green
        } else {
            az iot dps certificate create `
                --dps-name $DpsName `
                --resource-group $ResourceGroup `
                --certificate-name $rootCertName `
                --path $rootCertPath `
                --output none
        }

        if ($SkipVerification) {
            Write-Host "  ✓ Root CA uploaded (not verified)" -ForegroundColor Yellow
            $rootVerified = $false
        } else {
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
            $verificationKey = "$safeRegistrationId-verification.key"
            $verificationCsr = "$safeRegistrationId-verification.csr"
            $verificationPem = "$safeRegistrationId-verification.pem"
            Remove-Item $verificationKey, $verificationCsr, $verificationPem -ErrorAction SilentlyContinue
            openssl genrsa -out $verificationKey 2048
            openssl req -new -key $verificationKey -out $verificationCsr -subj "/CN=$code"
            openssl x509 -req -in $verificationCsr -CA $rootCertPath -CAkey $rootKeyPath -CAcreateserial -out $verificationPem -days 1 -sha256
            Pop-Location

            $verPem = Join-Path $rootDir $verificationPem

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
        }
    } catch {
        Write-Host "  ⚠ Root verification failed: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "[4/7] Skipping root upload/verify (provide -DpsName and -ResourceGroup)" -ForegroundColor Yellow
}

# Step 5: Upload & verify intermediate CA in DPS
$intermediateVerified = $false
if ($DpsName -and $ResourceGroup) {
    if ($SkipVerification) {
        Write-Host "[5/7] Uploading intermediate CA to DPS (verification skipped)..." -ForegroundColor Yellow
    } else {
        Write-Host "[5/7] Uploading and verifying intermediate CA in DPS..." -ForegroundColor Yellow
    }
    $intermediateCertName = "$CaRegistrationId-intermediate"
    try {
        $existing = az iot dps certificate show `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --certificate-name $intermediateCertName `
            -o json 2>$null

        if ($LASTEXITCODE -eq 0 -and $existing) {
            Write-Host "  ✓ Intermediate CA already exists in DPS: $intermediateCertName" -ForegroundColor Green
        } else {
            az iot dps certificate create `
                --dps-name $DpsName `
                --resource-group $ResourceGroup `
                --certificate-name $intermediateCertName `
                --path $caCertPath `
                --output none
        }

        if ($SkipVerification) {
            Write-Host "  ✓ Intermediate CA uploaded (not verified)" -ForegroundColor Yellow
            $intermediateVerified = $false
        } else {
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
            $verificationIntermediateKey = "$safeRegistrationId-verification-intermediate.key"
            $verificationIntermediateCsr = "$safeRegistrationId-verification-intermediate.csr"
            $verificationIntermediatePem = "$safeRegistrationId-verification-intermediate.pem"
            Remove-Item $verificationIntermediateKey, $verificationIntermediateCsr, $verificationIntermediatePem -ErrorAction SilentlyContinue
            openssl genrsa -out $verificationIntermediateKey 2048
            openssl req -new -key $verificationIntermediateKey -out $verificationIntermediateCsr -subj "/CN=$code"
            openssl x509 -req -in $verificationIntermediateCsr -CA $caCertPath -CAkey $caKeyPath -CAcreateserial -out $verificationIntermediatePem -days 1 -sha256
            Pop-Location

            $verPem = Join-Path $caDir $verificationIntermediatePem

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
        }
    } catch {
        Write-Host "  ⚠ Intermediate verification failed: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "[5/7] Skipping intermediate upload/verify (provide -DpsName and -ResourceGroup)" -ForegroundColor Yellow
}

# Step 6: Create DPS enrollment (if Azure CLI available and params provided)
if (-not $SkipEnrollment) {
    if ($DpsName -and $ResourceGroup) {
        Write-Host "[6/7] Ensuring DPS enrollment group exists (Intermediate attestation)..." -ForegroundColor Yellow
        
        # Check if Azure CLI is available
        if (Get-Command az -ErrorAction SilentlyContinue) {
            try {
                $existingEnrollment = az iot dps enrollment-group show `
                    --dps-name $DpsName `
                    --resource-group $ResourceGroup `
                    --enrollment-id $EnrollmentGroupId `
                    -o json 2>$null

                if ($LASTEXITCODE -eq 0 -and $existingEnrollment) {
                    Write-Host "  ✓ Enrollment group already exists: $EnrollmentGroupId (skipping create)" -ForegroundColor Green
                } else {
                    az iot dps enrollment-group create `
                        --dps-name $DpsName `
                        --resource-group $ResourceGroup `
                        --enrollment-id $EnrollmentGroupId `
                        --ca-name "$CaRegistrationId-intermediate" `
                        --credential-policy $CredentialPolicy `
                        --provisioning-status enabled `
                        --output none

                    Write-Host "  ✓ Enrollment group created with credential policy: $CredentialPolicy" -ForegroundColor Green
                    Write-Host "  ✓ CSR-based certificate issuance enabled" -ForegroundColor Green
                    Write-Host "  ✓ Using CA reference (intermediate must be verified)" -ForegroundColor Green
                }
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
        Write-Host "[6/7] Skipping DPS enrollment (no DPS details provided)" -ForegroundColor Yellow
        Write-Host "  Provide -DpsName and -ResourceGroup to auto-create" -ForegroundColor Gray
        $SkipEnrollment = $true
    }
} else {
    Write-Host "[6/7] Skipping DPS enrollment (manual setup required)" -ForegroundColor Yellow
}

# Step 7: Summary and next steps
Write-Host "`n[7/7] Setup Complete!" -ForegroundColor Green
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
Write-Host "Update appsettings.json with your RegistrationId and certificate paths:" -ForegroundColor Gray
Write-Host "  IoTHub:DpsProvisioning:RegistrationId = $RegistrationId" -ForegroundColor Gray
Write-Host "  IoTHub:DpsProvisioning:AttestationCertPath = $deviceCertPath" -ForegroundColor Gray
Write-Host "  IoTHub:DpsProvisioning:AttestationCertChainPath = $deviceFullChainPath" -ForegroundColor Gray
Write-Host "  IoTHub:DpsProvisioning:AttestationKeyPath = $deviceKeyPath" -ForegroundColor Gray
Write-Host "  Tip: Use tokenized paths like certs/.../{RegistrationId}-device.pem to switch identities by RegistrationId only" -ForegroundColor Gray

if ($SkipEnrollment) {
    Write-Host "`n=== Manual Steps Required ===" -ForegroundColor Yellow
    Write-Host "Create Enrollment Group with credential policy (intermediate attestation):"
    Write-Host "  az iot dps enrollment-group create"
    Write-Host "    --dps-name <DPS_NAME>"
    Write-Host "    --resource-group <RESOURCE_GROUP>"
    Write-Host "    --enrollment-id $EnrollmentGroupId"
    Write-Host "    --ca-name $CaRegistrationId-intermediate"
    Write-Host "    --credential-policy $CredentialPolicy"
    Write-Host ""
    Write-Host "  Root (verify in DPS): $rootThumbprint"
    Write-Host "  Intermediate (verify in DPS): $caThumbprint"
    Write-Host "  Leaf: $thumbprint"
} else {
    if ($SkipVerification) {
        Write-Host "\n=== Verification Status ===" -ForegroundColor Cyan
        Write-Host "Certificate verification was SKIPPED (-SkipVerification flag used)" -ForegroundColor Yellow
        Write-Host "ℹ Note: Verification is optional but recommended for production environments" -ForegroundColor Gray
    } else {
        Write-Host "\n=== Verification Status ===" -ForegroundColor Cyan
        Write-Host "Root CA isVerified: $rootVerified" -ForegroundColor $(if ($rootVerified) { "Green" } else { "Red" })
        Write-Host "Intermediate CA isVerified: $intermediateVerified" -ForegroundColor $(if ($intermediateVerified) { "Green" } else { "Red" })
        if (-not $rootVerified -or -not $intermediateVerified) {
            Write-Host "\n⚠ WARNING: Verification failed - this may cause provisioning issues!" -ForegroundColor Yellow
        }
    }
}

Write-Host "`n=== Test the Flow ===" -ForegroundColor Cyan
Write-Host "Run: dotnet run" -ForegroundColor White
Write-Host "`nExpected: Device authenticates with bootstrap cert, receives new cert via CSR`n"
