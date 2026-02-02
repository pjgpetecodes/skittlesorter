#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complete automation script for setting up Azure IoT with ADR integration and self-signed X.509 certificates.

.DESCRIPTION
    This script automates the entire process of:
    - Creating Azure resources (Resource Group, UAMI, ADR Namespace, IoT Hub, DPS)
    - Configuring ADR credential policies
    - Generating X.509 certificates (Root CA, Intermediate CA, Device)
    - Uploading and verifying certificates in DPS
    - Creating enrollment groups
    
    Based on the Microsoft guide: https://learn.microsoft.com/azure/iot-hub/iot-hub-device-registry-setup
    Blog series: Using Self-Signed X.509 Certificates with Azure IoT DPS and ADR Integration

.PARAMETER ResourceGroup
    Name of the Azure resource group to create or use.

.PARAMETER Location
    Azure region for resources (e.g., 'eastus', 'westus2').

.PARAMETER IoTHubName
    Name for the IoT Hub (must be globally unique).

.PARAMETER DPSName
    Name for the Device Provisioning Service (must be globally unique).

.PARAMETER AdrNamespace
    Name for the Azure Device Registry namespace (lowercase, globally unique).

.PARAMETER UserIdentity
    Name for the User-Assigned Managed Identity.

.PARAMETER AttestationType
    Type of attestation for device enrollment (default: X509).
    - X509: Bootstrap X.509 certificate for DPS attestation (device generates CSR for IoT Hub cert)
    - SymmetricKey: Symmetric key for DPS attestation (simpler, less secure)

.PARAMETER IoTHubSku
    IoT Hub pricing tier (default: GEN2 - required for ADR integration).

.PARAMETER CredentialPolicyName
    Name for the ADR credential policy (default: cert-policy).

.PARAMETER RegistrationId
    Device registration ID (default: skittlesorter).

.PARAMETER EnrollmentGroupId
    ID for the enrollment group (default: same as RegistrationId).

.PARAMETER CertsBasePath
    Base path for certificate storage (default: .\certs).

.PARAMETER SkipAzureSetup
    Skip Azure resource creation (use existing resources).

.PARAMETER SkipCertGeneration
    Skip certificate generation (use existing certificates).

.PARAMETER SkipEnrollment
    Skip enrollment group creation (manual setup required).

.EXAMPLE
    .\setup-x509-dps-adr.ps1 -ResourceGroup "iot-demo-rg" -Location "eastus" -IoTHubName "my-hub-001" -DPSName "my-dps-001" -AdrNamespace "my-adr-001" -UserIdentity "my-uami"

.EXAMPLE
    .\setup-x509-dps-adr.ps1 -ResourceGroup "iot-demo-rg" -Location "eastus" -IoTHubName "my-hub-001" -DPSName "my-dps-001" -AdrNamespace "my-adr-001" -UserIdentity "my-uami" -SkipAzureSetup

.NOTES
    Author: Generated for X.509 DPS + ADR Blog Series
    Requires: Azure CLI (with azure-iot extension 0.30.0b1+), OpenSSL, PowerShell 7+
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$Location,

    [Parameter(Mandatory = $true)]
    [string]$IoTHubName,

    [Parameter(Mandatory = $true)]
    [string]$DPSName,

    [Parameter(Mandatory = $true)]
    [string]$AdrNamespace,

    [Parameter(Mandatory = $true)]
    [string]$UserIdentity,

    [ValidateSet("X509", "SymmetricKey")]
    [string]$AttestationType = "X509",

    [string]$IoTHubSku = "GEN2",

    [string]$CredentialPolicyName = "cert-policy",

    [string]$RegistrationId = "skittlesorter",

    [string]$EnrollmentGroupId,

    [string]$CertsBasePath = ".\certs",

    [switch]$SkipAzureSetup,

    [switch]$SkipCertGeneration,

    [switch]$SkipEnrollment
)

# Error handling
$ErrorActionPreference = "Stop"

if (-not $EnrollmentGroupId) { $EnrollmentGroupId = $RegistrationId }

#region Logging Setup

$logFile = Join-Path $PSScriptRoot "setup-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$debugMode = $DebugPreference -eq "Continue"

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet("INFO", "SUCCESS", "WARNING", "ERROR", "DEBUG")]
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    # Write to console
    switch ($Level) {
        "SUCCESS" { Write-Host "✓ $Message" -ForegroundColor Green }
        "WARNING" { Write-Host "⚠ $Message" -ForegroundColor Yellow }
        "ERROR" { Write-Host "✗ $Message" -ForegroundColor Red }
        "DEBUG" { if ($debugMode) { Write-Host "🔍 $Message" -ForegroundColor Gray } }
        default { Write-Host "ℹ $Message" -ForegroundColor Yellow }
    }
    
    # Write to log file
    Add-Content -Path $logFile -Value $logMessage
}

Write-Log "Script started with parameters:" "DEBUG"
Write-Log "ResourceGroup: $ResourceGroup" "DEBUG"
Write-Log "Location: $Location" "DEBUG"
Write-Log "IoTHubName: $IoTHubName" "DEBUG"
Write-Log "DPSName: $DPSName" "DEBUG"
Write-Log "AdrNamespace: $AdrNamespace" "DEBUG"
Write-Log "UserIdentity: $UserIdentity" "DEBUG"
Write-Log "AttestationType: $AttestationType" "DEBUG"
Write-Log "RegistrationId: $RegistrationId" "DEBUG"
Write-Log "Log file: $logFile" "DEBUG"

#endregion

#region Helper Functions

function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Test-CommandExists {
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Test-AzureIoTExtension {
    $extensions = az extension list --query "[?name=='azure-iot'].name" -o tsv
    return $extensions -contains "azure-iot"
}

function Get-AzureIoTExtensionVersion {
    $extensions = az extension list | ConvertFrom-Json
    $iotExt = $extensions | Where-Object { $_.name -eq "azure-iot" }
    return $iotExt.version
}

#endregion

#region Prerequisites Check

Write-Step "Checking Prerequisites"

# Check Azure CLI
if (-not (Test-CommandExists "az")) {
    throw "Azure CLI is not installed. Please install from: https://aka.ms/InstallAzureCLIDocs"
}
Write-Log "Azure CLI found" "SUCCESS"

# Check OpenSSL
if (-not (Test-CommandExists "openssl")) {
    throw "OpenSSL is not installed. Install via Git for Windows or: winget install OpenSSL.Light"
}
Write-Log "OpenSSL found" "SUCCESS"

# Check Azure IoT extension
if (-not (Test-AzureIoTExtension)) {
    Write-Log "Installing Azure IoT extension with preview support..." "INFO"
    az extension add --name azure-iot --allow-preview
} else {
    $version = Get-AzureIoTExtensionVersion
    Write-Log "Azure IoT extension version: $version" "DEBUG"
    if ($version -lt "0.30.0") {
        Write-Log "Updating Azure IoT extension to support preview features..." "WARNING"
        az extension update --name azure-iot --allow-preview
    }
}
Write-Log "Azure IoT extension ready" "SUCCESS"

# Verify Azure login
Write-Log "Checking Azure login status..." "INFO"
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Log "Please login to Azure..." "INFO"
    az login
    $account = az account show | ConvertFrom-Json
}
Write-Log "Logged in as: $($account.user.name)" "SUCCESS"
Write-Log "Subscription: $($account.name)" "SUCCESS"

$subscriptionId = $account.id
Write-Log "Subscription ID: $subscriptionId" "DEBUG"

#endregion

#region Azure Resources Setup

if (-not $SkipAzureSetup) {
    Write-Step "Creating Azure Resources with ADR Integration"

    # Create Resource Group
    Write-Log "Creating resource group: $ResourceGroup" "INFO"
    az group create `
        --name $ResourceGroup `
        --location $Location `
        --output none
    Write-Log "Resource group created" "SUCCESS"

    # Configure App Privileges (IoT Hub app principal)
    Write-Log "Configuring IoT Hub app privileges..." "INFO"
    az role assignment create `
        --assignee "89d10474-74af-4874-99a7-c23c2f643083" `
        --role "Contributor" `
        --scope "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup" `
        --output none 2>$null
    Write-Log "App privileges configured" "SUCCESS"

    # Create User-Assigned Managed Identity
    Write-Log "Creating User-Assigned Managed Identity: $UserIdentity" "INFO"
    az identity create `
        --name $UserIdentity `
        --resource-group $ResourceGroup `
        --location $Location `
        --output none
    
    $uamiResourceId = az identity show `
        --name $UserIdentity `
        --resource-group $ResourceGroup `
        --query id -o tsv
    
    $uamiPrincipalId = az identity show `
        --name $UserIdentity `
        --resource-group $ResourceGroup `
        --query principalId -o tsv
    
    Write-Log "UAMI created with Principal ID: $uamiPrincipalId" "SUCCESS"

    # Create ADR Namespace with System-Assigned Identity and Default Policy
    Write-Log "Creating ADR Namespace: $AdrNamespace (this may take up to 5 minutes...)" "INFO"
    az iot adr ns create `
        --name $AdrNamespace `
        --resource-group $ResourceGroup `
        --location $Location `
        --enable-credential-policy true `
        --policy-name $CredentialPolicyName `
        --output none
    
    $namespaceResourceId = az iot adr ns show `
        --name $AdrNamespace `
        --resource-group $ResourceGroup `
        --query id -o tsv
    
    Write-Log "ADR Namespace created" "SUCCESS"

    # Assign UAMI role to access the ADR namespace
    Write-Log "Assigning Azure Device Registry Contributor role to UAMI..." "INFO"
    az role assignment create `
        --assignee $uamiPrincipalId `
        --role "a5c3590a-3a1a-4cd4-9648-ea0a32b15137" `
        --scope $namespaceResourceId `
        --output none
    Write-Log "UAMI role assigned" "SUCCESS"

    # Create IoT Hub with ADR integration
    Write-Log "Creating IoT Hub: $IoTHubName (this may take a few minutes...)" "INFO"
    az iot hub create `
        --name $IoTHubName `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku $IoTHubSku `
        --mi-user-assigned $uamiResourceId `
        --ns-resource-id $namespaceResourceId `
        --ns-identity-id $uamiResourceId `
        --output none
    
    $hubResourceId = az iot hub show `
        --name $IoTHubName `
        --resource-group $ResourceGroup `
        --query id -o tsv
    
    Write-Log "IoT Hub created" "SUCCESS"

    # Assign IoT Hub roles to access the ADR namespace
    Write-Log "Configuring ADR to IoT Hub permissions..." "INFO"
    $adrPrincipalId = az iot adr ns show `
        --name $AdrNamespace `
        --resource-group $ResourceGroup `
        --query identity.principalId -o tsv
    
    az role assignment create `
        --assignee $adrPrincipalId `
        --role "Contributor" `
        --scope $hubResourceId `
        --output none
    
    az role assignment create `
        --assignee $adrPrincipalId `
        --role "IoT Hub Registry Contributor" `
        --scope $hubResourceId `
        --output none
    
    Write-Log "IoT Hub roles configured" "SUCCESS"

    # Create DPS with ADR integration
    Write-Log "Creating Device Provisioning Service: $DPSName" "INFO"
    az iot dps create `
        --name $DPSName `
        --resource-group $ResourceGroup `
        --location $Location `
        --mi-user-assigned $uamiResourceId `
        --ns-resource-id $namespaceResourceId `
        --ns-identity-id $uamiResourceId `
        --output none
    Write-Log "DPS created" "SUCCESS"

    # Link DPS to IoT Hub
    Write-Log "Linking DPS to IoT Hub..." "INFO"
    az iot dps linked-hub create `
        --dps-name $DPSName `
        --resource-group $ResourceGroup `
        --hub-name $IoTHubName `
        --output none
    Write-Log "DPS linked to IoT Hub" "SUCCESS"

    # Sync credentials and policies to IoT Hub
    Write-Log "Syncing ADR credentials and policies to IoT Hub..." "INFO"
    az iot adr ns credential sync `
        --namespace $AdrNamespace `
        --resource-group $ResourceGroup `
        --output none
    Write-Log "Credentials synced" "SUCCESS"

    # Validate IoT Hub CA certificate
    Write-Log "Validating IoT Hub CA certificate registration..." "INFO"
    $hubCerts = az iot hub certificate list `
        --hub-name $IoTHubName `
        --resource-group $ResourceGroup `
        -o json | ConvertFrom-Json -AsHashTable
    
    if ($hubCerts -and $hubCerts.Count -gt 0) {
        Write-Log "Hub CA certificate registered successfully" "SUCCESS"
    } else {
        Write-Log "Warning: No CA certificates found on hub" "WARNING"
    }

    # Get DPS ID Scope
    $idScope = az iot dps show `
        --name $DPSName `
        --resource-group $ResourceGroup `
        --query "properties.idScope" `
        -o tsv
    Write-Log "DPS ID Scope: $idScope" "SUCCESS"

} else {
    Write-Log "Skipping Azure resource creation (using existing resources)" "INFO"
    
    # Get existing resource IDs
    $idScope = az iot dps show `
        --name $DPSName `
        --resource-group $ResourceGroup `
        --query "properties.idScope" `
        -o tsv
    Write-Log "Using existing DPS with ID Scope: $idScope" "INFO"
}

#endregion

#region Bootstrap Attestation Setup

if ($AttestationType -eq "X509") {
    if (-not $SkipCertGeneration) {
        Write-Step "Generating Bootstrap X.509 Certificate"

        # Create directory structure
        Write-Log "Creating bootstrap certificate directory structure..." "INFO"
        $rootDir = Join-Path $PSScriptRoot $CertsBasePath "root"
        $caDir = Join-Path $PSScriptRoot $CertsBasePath "ca"
        $deviceDir = Join-Path $PSScriptRoot $CertsBasePath "device"
        $issuedDir = Join-Path $PSScriptRoot $CertsBasePath "issued"

        New-Item -ItemType Directory -Path $rootDir -Force | Out-Null
        New-Item -ItemType Directory -Path $caDir -Force | Out-Null
        New-Item -ItemType Directory -Path $deviceDir -Force | Out-Null
        New-Item -ItemType Directory -Path $issuedDir -Force | Out-Null
        
        Write-Log "Certificate directories created" "SUCCESS"
        Write-Log "Root Dir: $rootDir" "DEBUG"
        Write-Log "CA Dir: $caDir" "DEBUG"
        Write-Log "Device Dir: $deviceDir" "DEBUG"
        Write-Log "Issued Dir: $issuedDir" "DEBUG"

        # Certificate paths
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

        # Generate Root CA (self-signed, pathlen 1)
        Write-Log "Generating Root CA: $RegistrationId-root" "INFO"
        Write-Log "Running: openssl req -x509 for Root CA" "DEBUG"
    
    $opensslOutput = openssl req -x509 -new -nodes -newkey rsa:4096 `
        -keyout "$rootKeyPath" `
        -out "$rootCertPath" `
        -days 3650 -sha256 `
        -subj "/CN=$RegistrationId-root" `
        -addext "basicConstraints=critical,CA:true,pathlen:1" `
        -addext "keyUsage=critical,keyCertSign,cRLSign" `
        -addext "subjectKeyIdentifier=hash" `
        -addext "authorityKeyIdentifier=keyid:always" 2>&1
    
    Write-Log "OpenSSL output: $opensslOutput" "DEBUG"
    
    if (-not (Test-Path $rootCertPath)) {
        throw "Failed to generate root CA certificate"
    }
    Write-Log "Root CA generated" "SUCCESS"

    # Generate Intermediate CA
    Write-Log "Generating Intermediate CA: $RegistrationId-intermediate" "INFO"
    
    # Intermediate CA private key and CSR
    Write-Log "Running: openssl req for Intermediate CA CSR" "DEBUG"
    $opensslOutput = openssl req -new -nodes -newkey rsa:4096 `
        -keyout "$caKeyPath" `
        -out "$caCsrPath" `
        -subj "/CN=$RegistrationId-intermediate" 2>&1
    Write-Log "OpenSSL output: $opensslOutput" "DEBUG"
    
    # Create extension file for Intermediate CA
    $caExtPath = Join-Path $caDir "ca-ext.cnf"
    @"
[ v3_intermediate ]
basicConstraints = critical,CA:true,pathlen:0
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path $caExtPath -Encoding ASCII
    Write-Log "Created extension file: $caExtPath" "DEBUG"
    
    # Sign Intermediate CA with Root CA
    Write-Log "Running: openssl x509 to sign Intermediate CA" "DEBUG"
    $opensslOutput = openssl x509 -req `
        -in "$caCsrPath" `
        -CA "$rootCertPath" `
        -CAkey "$rootKeyPath" `
        -CAcreateserial `
        -out "$caCertPath" `
        -days 1825 -sha256 `
        -extfile "$caExtPath" -extensions v3_intermediate 2>&1
    Write-Log "OpenSSL output: $opensslOutput" "DEBUG"
    
    if (-not (Test-Path $caCertPath)) {
        throw "Failed to generate intermediate CA certificate"
    }
    Write-Log "Intermediate CA generated" "SUCCESS"

    # Generate Device Certificate (bootstrap cert for DPS attestation)
    Write-Log "Generating Device Certificate (bootstrap cert for DPS attestation): $RegistrationId" "INFO"
    
    # Device private key and CSR
    Write-Log "Running: openssl req for Device CSR" "DEBUG"
    $opensslOutput = openssl req -new -nodes -newkey rsa:2048 `
        -keyout "$deviceKeyPath" `
        -out "$deviceCsrPath" `
        -subj "/CN=$RegistrationId" 2>&1
    Write-Log "OpenSSL output: $opensslOutput" "DEBUG"
    
    # Create extension file for Device Certificate
    $deviceExtPath = Join-Path $deviceDir "device-ext.cnf"
    @"
[ v3_req ]
basicConstraints = CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = clientAuth
subjectAltName = DNS:$RegistrationId
authorityKeyIdentifier = keyid:always,issuer
"@ | Set-Content -Path $deviceExtPath -Encoding ASCII
    Write-Log "Created extension file: $deviceExtPath" "DEBUG"
    
    # Sign Device certificate with Intermediate CA
    Write-Log "Running: openssl x509 to sign Device certificate" "DEBUG"
    $opensslOutput = openssl x509 -req `
        -in "$deviceCsrPath" `
        -CA "$caCertPath" `
        -CAkey "$caKeyPath" `
        -CAcreateserial `
        -out "$deviceCertPath" `
        -days 365 -sha256 `
        -extfile "$deviceExtPath" -extensions v3_req 2>&1
    Write-Log "OpenSSL output: $opensslOutput" "DEBUG"
    
    if (-not (Test-Path $deviceCertPath)) {
        throw "Failed to generate device certificate"
    }
    Write-Log "Device certificate generated" "SUCCESS"

    # Create certificate chains
    Write-Log "Creating certificate chains..." "INFO"
    
    # Chain file (intermediate + root) for TLS presentation
    Get-Content $caCertPath, $rootCertPath | Set-Content -Path $chainPath -Encoding ASCII
    
    # Full chain (device + intermediate + root)
    Get-Content $deviceCertPath, $caCertPath, $rootCertPath | Set-Content -Path $deviceFullChainPath -Encoding ASCII
    
    Write-Log "Certificate chains created" "SUCCESS"

    # Verify certificate chain
    Write-Log "Verifying certificate chain..." "INFO"
    $verification = openssl verify `
        -CAfile "$rootCertPath" `
        -untrusted "$caCertPath" `
        "$deviceCertPath" 2>&1
    Write-Log "Verification output: $verification" "DEBUG"
    
    if ($verification -like "*OK*") {
        Write-Log "Certificate chain verified successfully" "SUCCESS"
    } else {
        Write-Log "Certificate verification returned: $verification" "WARNING"
    }

    # Get certificate thumbprints
    Write-Log "Extracting certificate thumbprints..." "INFO"
    
    $rootX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($rootCertPath)
    $rootThumbprint = $rootX509.Thumbprint
    
    $caX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($caCertPath)
    $caThumbprint = $caX509.Thumbprint
    
    $deviceX509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($deviceCertPath)
    $deviceThumbprint = $deviceX509.Thumbprint
    
    Write-Log "Root Thumbprint: $rootThumbprint" "DEBUG"
    Write-Log "Intermediate Thumbprint: $caThumbprint" "DEBUG"
    Write-Log "Device Thumbprint: $deviceThumbprint" "DEBUG"
    
    } else {
        Write-Log "Skipping certificate generation (using existing certificates)" "INFO"
        
        # Set paths for existing certificates
        $rootDir = Join-Path $PSScriptRoot $CertsBasePath "root"
        $caDir = Join-Path $PSScriptRoot $CertsBasePath "ca"
        $deviceDir = Join-Path $PSScriptRoot $CertsBasePath "device"
        $issuedDir = Join-Path $PSScriptRoot $CertsBasePath "issued"
        
        $rootCertPath = Join-Path $rootDir "root.pem"
        $rootKeyPath = Join-Path $rootDir "root.key"
        $caCertPath = Join-Path $caDir "ca.pem"
        $caKeyPath = Join-Path $caDir "ca.key"
        $deviceCertPath = Join-Path $deviceDir "device.pem"
        $deviceKeyPath = Join-Path $deviceDir "device.key"
        $deviceFullChainPath = Join-Path $deviceDir "device-full-chain.pem"
    }

} elseif ($AttestationType -eq "SymmetricKey") {
    Write-Step "Generating Bootstrap Symmetric Key"
    
    Write-Log "Generating symmetric key for device attestation..." "INFO"
    
    # Generate a 64-character (32-byte) symmetric key in Base64
    $keyBytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)
    $primaryKey = [Convert]::ToBase64String($keyBytes)
    
    # Generate secondary key for rotation
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)
    $secondaryKey = [Convert]::ToBase64String($keyBytes)
    
    Write-Log "Symmetric keys generated" "SUCCESS"
    Write-Log "Primary Key: $primaryKey" "DEBUG"
    Write-Log "Secondary Key: $secondaryKey" "DEBUG"

} else {
    throw "Invalid AttestationType: $AttestationType"
}

#endregion

#endregion

#region Upload CA Certificates to DPS (for X.509 bootstrap attestation)

if ($AttestationType -eq "X509") {
    Write-Step "Uploading and Verifying CA Certificates in DPS"

    # Upload Root CA for certificate chain validation
    Write-Log "Uploading Root CA to DPS: $RegistrationId-root" "INFO"
    
    $rootCertName = "$RegistrationId-root"
    az iot dps certificate create `
        --dps-name $DPSName `
        --resource-group $ResourceGroup `
        --certificate-name $rootCertName `
        --path "$rootCertPath" `
        --output none 2>$null

    if ($LASTEXITCODE -eq 0) {
        Write-Log "Root CA uploaded to DPS" "SUCCESS"
        
        # Generate verification code for Root CA
        Write-Log "Generating verification code for Root CA..." "INFO"
        try {
            $cert = az iot dps certificate show `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $rootCertName `
                -o json 2>$null | ConvertFrom-Json
            $etag = if ($cert.properties -and $cert.properties.etag) { $cert.properties.etag } else { $cert.etag }

            $ver = az iot dps certificate generate-verification-code `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $rootCertName `
                --etag $etag `
                -o json 2>$null | ConvertFrom-Json
            $code = $ver.properties.verificationCode
            $etag = if ($ver.properties -and $ver.properties.etag) { $ver.properties.etag } else { $ver.etag }

            Write-Log "Verification Code: $code" "INFO"

            # Create verification certificate
            $rootVerDir = Split-Path $rootCertPath
            Push-Location $rootVerDir
            Remove-Item verification.key, verification.csr, verification.pem, root.pem.srl -ErrorAction SilentlyContinue
            openssl genrsa -out verification.key 2048 2>$null
            openssl req -new -key verification.key -out verification.csr -subj "/CN=$code" 2>$null
            openssl x509 -req -in verification.csr -CA root.pem -CAkey $rootKeyPath -CAcreateserial -out verification.pem -days 1 -sha256 2>$null
            Pop-Location

            $verPem = Join-Path $rootVerDir "verification.pem"

            # Verify Root CA in DPS
            az iot dps certificate verify `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $rootCertName `
                --path $verPem `
                --etag $etag `
                --output none 2>$null

            $final = az iot dps certificate show `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $rootCertName `
                -o json 2>$null | ConvertFrom-Json
            $rootVerified = $final.properties.isVerified
            Write-Log "Root CA isVerified: $rootVerified" $(if ($rootVerified) { "SUCCESS" } else { "WARNING" })
        } catch {
            Write-Log "Root CA verification failed: $($_.Exception.Message)" "WARNING"
        }
    } else {
        Write-Log "Root CA may already exist in DPS" "INFO"
    }

    # Upload Intermediate CA for bootstrap X.509 attestation validation
    Write-Log "Uploading Intermediate CA to DPS: $RegistrationId-intermediate" "INFO"
    
    $caCertName = "$RegistrationId-intermediate"
    az iot dps certificate create `
        --dps-name $DPSName `
        --resource-group $ResourceGroup `
        --certificate-name $caCertName `
        --path "$caCertPath" `
        --output none 2>$null

    if ($LASTEXITCODE -eq 0) {
        Write-Log "Intermediate CA uploaded to DPS" "SUCCESS"
        
        # Generate verification code for Intermediate CA
        Write-Log "Generating verification code for Intermediate CA..." "INFO"
        try {
            $cert = az iot dps certificate show `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $caCertName `
                -o json 2>$null | ConvertFrom-Json
            $etag = if ($cert.properties -and $cert.properties.etag) { $cert.properties.etag } else { $cert.etag }

            $ver = az iot dps certificate generate-verification-code `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $caCertName `
                --etag $etag `
                -o json 2>$null | ConvertFrom-Json
            $code = $ver.properties.verificationCode
            $etag = if ($ver.properties -and $ver.properties.etag) { $ver.properties.etag } else { $ver.etag }

            Write-Log "Verification Code: $code" "INFO"

            # Create verification certificate
            $caVerDir = Split-Path $caCertPath
            Push-Location $caVerDir
            Remove-Item verification-intermediate.key, verification-intermediate.csr, verification-intermediate.pem, ca.pem.srl -ErrorAction SilentlyContinue
            openssl genrsa -out verification-intermediate.key 2048 2>$null
            openssl req -new -key verification-intermediate.key -out verification-intermediate.csr -subj "/CN=$code" 2>$null
            openssl x509 -req -in verification-intermediate.csr -CA ca.pem -CAkey $caKeyPath -CAcreateserial -out verification-intermediate.pem -days 1 -sha256 2>$null
            Pop-Location

            $verPem = Join-Path $caVerDir "verification-intermediate.pem"

            # Verify Intermediate CA in DPS
            az iot dps certificate verify `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $caCertName `
                --path $verPem `
                --etag $etag `
                --output none 2>$null

            $final = az iot dps certificate show `
                --dps-name $DPSName `
                --resource-group $ResourceGroup `
                --certificate-name $caCertName `
                -o json 2>$null | ConvertFrom-Json
            $intermediateVerified = $final.properties.isVerified
            Write-Log "Intermediate CA isVerified: $intermediateVerified" $(if ($intermediateVerified) { "SUCCESS" } else { "WARNING" })
        } catch {
            Write-Log "Intermediate CA verification failed: $($_.Exception.Message)" "WARNING"
        }
    } else {
        Write-Log "Intermediate CA may already exist in DPS" "INFO"
    }
}

#endregion

#region Device Runtime: CSR-Based Certificate Signing (via DPS + ADR)

Write-Step "CSR-Based Certificate Signing (Device Runtime)"

Write-Log "Certificate signing workflow:" "INFO"
Write-Log "1. Device uses bootstrap credential (X.509 cert or symmetric key) to authenticate to DPS" "INFO"
Write-Log "2. DPS validates bootstrap credential:" "INFO"
if ($AttestationType -eq "X509") {
    Write-Log "   - X.509: Validates certificate against uploaded CA: $RegistrationId-intermediate" "INFO"
} else {
    Write-Log "   - Symmetric Key: Validates key against enrollment group" "INFO"
}
Write-Log "3. Device submits CSR to DPS for the IoT Hub certificate" "INFO"
Write-Log "4. DPS forwards CSR to ADR for signing using credential policy: $CredentialPolicyName" "INFO"
Write-Log "5. ADR signs the CSR and returns the certificate chain" "INFO"
Write-Log "6. Device receives certificate and uses it to connect to IoT Hub" "INFO"
Write-Log "" "INFO"
Write-Log "This CSR workflow is handled by your device application at runtime." "INFO"
Write-Log "See the device provisioning code for CSR submission implementation." "SUCCESS"

#endregion

#region Create Enrollment Group

if (-not $SkipEnrollment) {
    Write-Step "Creating Enrollment Group"

    Write-Log "Creating enrollment group: $EnrollmentGroupId (Attestation: $AttestationType)" "INFO"
    
    if ($AttestationType -eq "X509") {
        # Create enrollment group with bootstrap CA and credential policy for X.509 CSR-based provisioning
        Write-Log "Using X.509 bootstrap attestation with CA: $RegistrationId-intermediate and policy: $CredentialPolicyName" "DEBUG"
        
        az iot dps enrollment-group create `
            --dps-name $DPSName `
            --resource-group $ResourceGroup `
            --enrollment-id $EnrollmentGroupId `
            --ca-name "$RegistrationId-intermediate" `
            --credential-policy $CredentialPolicyName `
            --provisioning-status enabled `
            --output none 2>$null

    } elseif ($AttestationType -eq "SymmetricKey") {
        # Create enrollment group using symmetric keys with credential policy
        Write-Log "Using symmetric key attestation with credential policy: $CredentialPolicyName" "DEBUG"
        
        az iot dps enrollment-group create `
            --dps-name $DPSName `
            --resource-group $ResourceGroup `
            --enrollment-id $EnrollmentGroupId `
            --primary-key "$primaryKey" `
            --secondary-key "$secondaryKey" `
            --provisioning-status enabled `
            --credential-policy $CredentialPolicyName `
            --output none 2>$null
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Enrollment group may already exist" "INFO"
    } else {
        Write-Log "Enrollment group created" "SUCCESS"
    }
} else {
    Write-Log "Skipping enrollment group creation" "INFO"
}

#endregion

#region Summary

Write-Step "Setup Complete!"

Write-Host @"

✓ Azure Resources Created:
  - Resource Group: $ResourceGroup
  - User-Assigned Managed Identity: $UserIdentity
  - ADR Namespace: $AdrNamespace
  - Credential Policy: $CredentialPolicyName
  - IoT Hub: $IoTHubName (SKU: $IoTHubSku)
  - DPS: $DPSName
  - DPS ID Scope: $idScope

✓ Bootstrap Attestation Setup:
  - Attestation Type: $AttestationType
"@ -ForegroundColor Green

if ($AttestationType -eq "X509") {
    Write-Host @"
  - Bootstrap X.509 Certificate: $deviceCertPath
  - Bootstrap Private Key: $deviceKeyPath
  - Full Chain: $deviceFullChainPath
  - Root CA: $rootCertPath
  - Intermediate CA: $caCertPath

✓ Device Runtime Workflow:
  1. Device uses bootstrap X.509 certificate to authenticate to DPS
  2. Device generates CSR and submits to DPS
  3. DPS forwards CSR to ADR for signing
  4. ADR signs CSR using credential policy: $CredentialPolicyName
  5. Device receives signed certificate for IoT Hub
  6. Device connects to IoT Hub with signed certificate

Next Steps:
1. Configure device with bootstrap certificate:
   - Bootstrap Cert: $deviceCertPath
   - Bootstrap Key: $deviceKeyPath
   
2. Update your appsettings.json with:
   - ID Scope: $idScope
   - Registration ID: $RegistrationId
   - ADR Subscription: $subscriptionId
   - ADR Resource Group: $ResourceGroup
   - ADR Namespace: $AdrNamespace
   
3. Implement CSR submission in device code (see documentation)

4. Run your device application to:
   - Authenticate to DPS with bootstrap certificate
   - Request IoT Hub certificate via CSR
   - Connect to IoT Hub with signed certificate

"@ -ForegroundColor Green
} elseif ($AttestationType -eq "SymmetricKey") {
    Write-Host @"
  - Primary Key: $primaryKey
  - Secondary Key: $secondaryKey

✓ Device Runtime Workflow:
  1. Device uses primary/secondary symmetric keys to authenticate to DPS
  2. Device generates CSR and submits to DPS
  3. DPS forwards CSR to ADR for signing
  4. ADR signs CSR using credential policy: $CredentialPolicyName
  5. Device receives signed certificate for IoT Hub
  6. Device connects to IoT Hub with signed certificate

Next Steps:
1. Configure device with symmetric keys:
   - Primary Key: $primaryKey
   - Secondary Key: $secondaryKey
   
2. Update your appsettings.json with:
   - ID Scope: $idScope
   - Registration ID: $RegistrationId
   - Primary Key: $primaryKey
   - Secondary Key: $secondaryKey
   - ADR Subscription: $subscriptionId
   - ADR Resource Group: $ResourceGroup
   - ADR Namespace: $AdrNamespace
   
3. Implement CSR submission in device code (see documentation)

4. Run your device application to:
   - Authenticate to DPS with symmetric key
   - Request IoT Hub certificate via CSR
   - Connect to IoT Hub with signed certificate

"@ -ForegroundColor Green
}

Write-Host @"
For more details, see the blog series and Microsoft documentation:
- https://learn.microsoft.com/azure/iot-hub/iot-hub-device-registry-setup
- Blog series: Using Self-Signed X.509 Certificates with Azure IoT DPS

"@ -ForegroundColor Green

#endregion
