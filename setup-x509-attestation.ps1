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
.PARAMETER SkipEnrollment
    Skip creating DPS enrollment group (manual setup required)
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$RegistrationId,
    
    [string]$DpsName,
    [string]$ResourceGroup,
    [string]$CredentialPolicy = "default",
    [switch]$SkipEnrollment
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== X.509 Attestation Setup for Azure DPS ===" -ForegroundColor Cyan
Write-Host "Registration ID: $RegistrationId`n"

# Step 1: Create certs directory structure
Write-Host "[1/6] Creating certs directory structure..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "certs/bootstrap" | Out-Null
New-Item -ItemType Directory -Force -Path "certs/issued" | Out-Null

# Step 2: Generate bootstrap certificate using .NET
Write-Host "[2/6] Generating bootstrap X.509 certificate..." -ForegroundColor Yellow

$certPath = "certs/bootstrap/bootstrap-cert.pem"
$keyPath = "certs/bootstrap/bootstrap-key.pem"

# Build the certificate generator
dotnet build --configuration Release --verbosity quiet | Out-Null

# Use the built assembly to generate certificate
$generateScript = @"
using System;
using System.IO;
using AzureDpsFramework;

var (certPem, keyPem) = CertificateManager.GenerateSelfSignedCertificate(
    `"$RegistrationId-bootstrap`",
    365,
    `"RSA`",
    2048
);

CertificateManager.SaveText(`"$certPath`", certPem);
CertificateManager.SaveText(`"$keyPath`", keyPem);

Console.WriteLine(certPem);
"@

$tempCs = [System.IO.Path]::GetTempFileName() + ".csx"
Set-Content -Path $tempCs -Value $generateScript

# Run via dotnet-script or compile inline
try {
    # Try using C# script if dotnet-script is available
    if (Get-Command dotnet-script -ErrorAction SilentlyContinue) {
        dotnet-script $tempCs --no-cache 2>&1 | Out-Null
    } else {
        # Fallback: Use PowerShell .NET to generate
        Add-Type -Path "bin/Debug/net10.0/AzureDpsFramework.dll"
        
        $result = [AzureDpsFramework.CertificateManager]::GenerateSelfSignedCertificate(
            "$RegistrationId-bootstrap",
            365,
            "RSA",
            2048
        )
        
        [AzureDpsFramework.CertificateManager]::SaveText($certPath, $result.Item1)
        [AzureDpsFramework.CertificateManager]::SaveText($keyPath, $result.Item2)
        
        Write-Host "Certificate: $certPath" -ForegroundColor Green
        Write-Host "Private Key: $keyPath" -ForegroundColor Green
    }
} finally {
    Remove-Item $tempCs -ErrorAction SilentlyContinue
}

# Step 3: Get certificate thumbprint
Write-Host "[3/6] Extracting certificate thumbprint..." -ForegroundColor Yellow

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath)
$thumbprint = $cert.Thumbprint
$subject = $cert.Subject

Write-Host "  Subject: $subject" -ForegroundColor Gray
Write-Host "  Thumbprint: $thumbprint" -ForegroundColor Green
Write-Host "  Valid: $($cert.NotBefore) to $($cert.NotAfter)" -ForegroundColor Gray

# Step 4: Create DPS enrollment (if Azure CLI available and params provided)
if (-not $SkipEnrollment) {
    if ($DpsName -and $ResourceGroup) {
        Write-Host "[4/6] Creating DPS enrollment group with credential policy..." -ForegroundColor Yellow
        
        # Check if Azure CLI is available
        if (Get-Command az -ErrorAction SilentlyContinue) {
            try {
                # Create enrollment group with X.509 attestation and credential policy for CSR support
                az iot dps enrollment-group create `
                    --dps-name $DpsName `
                    --resource-group $ResourceGroup `
                    --enrollment-group-id $RegistrationId `
                    --attestation-type x509 `
                    --certificate-path $certPath `
                    --credential-policy $CredentialPolicy `
                    --output none
                
                Write-Host "  ✓ Enrollment group created with credential policy: $CredentialPolicy" -ForegroundColor Green
                Write-Host "  ✓ CSR-based certificate issuance enabled" -ForegroundColor Green
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
        Write-Host "[4/6] Skipping DPS enrollment (no DPS details provided)" -ForegroundColor Yellow
        Write-Host "  Provide -DpsName and -ResourceGroup to auto-create" -ForegroundColor Gray
        $SkipEnrollment = $true
    }
} else {
    Write-Host "[4/6] Skipping DPS enrollment (manual setup required)" -ForegroundColor Yellow
}

# Step 5: Update appsettings.json
Write-Host "[5/6] Updating appsettings.json..." -ForegroundColor Yellow

$appsettingsPath = "appsettings.json"
if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    
    # Update DPS provisioning settings
    $appsettings.IoTHub.DpsProvisioning.AttestationMethod = "X509"
    $appsettings.IoTHub.DpsProvisioning.RegistrationId = $RegistrationId
    $appsettings.IoTHub.DpsProvisioning.AttestationCertPath = $certPath
    $appsettings.IoTHub.DpsProvisioning.AttestationKeyPath = $keyPath
    $appsettings.IoTHub.DpsProvisioning.AttestationCertChainPath = ""
    $appsettings.IoTHub.DpsProvisioning.EnrollmentGroupKeyBase64 = ""
    $appsettings.IoTHub.DpsProvisioning.EnableDebugLogging = $true
    
    # Save updated settings
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath
    
    Write-Host "  ✓ appsettings.json updated for X.509 attestation" -ForegroundColor Green
} else {
    Write-Host "  ⚠ appsettings.json not found" -ForegroundColor Red
}

# Step 6: Summary and next steps
Write-Host "`n[6/6] Setup Complete!" -ForegroundColor Green
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Bootstrap Certificate: $certPath"
Write-Host "Private Key: $keyPath"
Write-Host "Thumbprint: $thumbprint"
Write-Host "Registration ID: $RegistrationId"
Write-Host "`nAttestation Method: X509" -ForegroundColor Green

if ($SkipEnrollment) {
    Write-Host "`n=== Manual Steps Required ===" -ForegroundColor Yellow
    Write-Host "Create Enrollment Group with credential policy:"
    Write-Host "  az iot dps enrollment-group create"
    Write-Host "    --dps-name <DPS_NAME>"
    Write-Host "    --resource-group <RESOURCE_GROUP>"
    Write-Host "    --enrollment-group-id $RegistrationId"
    Write-Host "    --attestation-type x509"
    Write-Host "    --certificate-path $certPath"
    Write-Host "    --credential-policy $CredentialPolicy"
    Write-Host ""
    Write-Host "  Or use thumbprint: $thumbprint"
}

Write-Host "`n=== Test the Flow ===" -ForegroundColor Cyan
Write-Host "Run: dotnet run" -ForegroundColor White
Write-Host "`nExpected: Device authenticates with bootstrap cert, receives new cert via CSR`n"
