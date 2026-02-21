#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)][string]$DpsName,
    [Parameter(Mandatory=$true)][string]$IotHubName,
    [Parameter(Mandatory=$false)][string[]]$AdditionalIotHubNames = @(),
    [Parameter(Mandatory=$true)][string]$ResourceGroup,
    [Parameter(Mandatory=$true)][string]$EnrollmentGroupName,
    [Parameter(Mandatory=$true)][string]$DeviceId,
    [Parameter(Mandatory=$true)][string]$AdrNamespace,
    [Parameter(Mandatory=$true)][string]$UserIdentity,
    [Parameter(Mandatory=$false)][string]$RootCertName = "root-ca",
    [Parameter(Mandatory=$false)][string]$IntermediateCertName = "intermediate-ca",
    [Parameter(Mandatory=$false)][bool]$DeleteResourceGroup = $true,
    [switch]$NoWaitForResourceGroupDeletion,
    [switch]$DryRun,
    [switch]$NoQuiet
)

$ErrorActionPreference = "Continue"

function Remove-Quiet {
    param(
        [scriptblock]$Action,
        [string]$Description
    )
    try {
        Write-Host $Description -ForegroundColor Cyan
        if ($DryRun) {
            Write-Host "  -> WHATIF (dry run enabled; no changes made)" -ForegroundColor Yellow
            return
        }
        $global:LASTEXITCODE = 0
        if ($NoQuiet) {
            & $Action
        } else {
            & $Action | Out-Null
        }
        if (-not $?) {
            Write-Host "  -> FAILED" -ForegroundColor Red
        } elseif ($LASTEXITCODE -ne 0) {
            Write-Host "  -> FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        } else {
            Write-Host "  -> OK" -ForegroundColor Green
        }
    } catch {
        Write-Host "  -> ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Get-DpsCertEtag {
    param(
        [string]$CertName
    )

    $etag = az iot dps certificate show `
        --dps-name $DpsName `
        --resource-group $ResourceGroup `
        --name $CertName `
        --query etag `
        -o tsv 2>$null

    return $etag
}

function Remove-DpsCert {
    param(
        [string]$CertName
    )

    $etag = Get-DpsCertEtag -CertName $CertName
    if (-not $etag) {
        Write-Host "Removing DPS CA certificate: $CertName..." -ForegroundColor Cyan
        Write-Host "  -> SKIP (not found or no etag)" -ForegroundColor Yellow
        return
    }

    Remove-Quiet {
        az iot dps certificate delete `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --name $CertName `
            --etag $etag
    } "Removing DPS CA certificate: $CertName..."
}

function Supports-AdrDelete {
    try {
        az iot dps namespace device delete --help *> $null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

Write-Host "Parameters:" -ForegroundColor Yellow
Write-Host "  DPS:                $DpsName"
Write-Host "  IoT Hub:            $IotHubName"
if ($AdditionalIotHubNames.Count -gt 0) {
    Write-Host "  Extra IoT Hubs:     $($AdditionalIotHubNames -join ', ')"
}
Write-Host "  Resource Group:     $ResourceGroup"
Write-Host "  Enrollment Group:   $EnrollmentGroupName"
Write-Host "  DeviceId:           $DeviceId"
Write-Host "  ADR Namespace:      $AdrNamespace"
Write-Host "  User Identity:      $UserIdentity"
Write-Host "  Root Cert Name:     $RootCertName"
Write-Host "  Int Cert Name:      $IntermediateCertName"
Write-Host "  Delete RG:          $DeleteResourceGroup"
Write-Host "  RG Delete No-Wait:  $($NoWaitForResourceGroupDeletion.IsPresent)"
Write-Host "  Dry Run:            $($DryRun.IsPresent)"
Write-Host "  No Quiet:           $($NoQuiet.IsPresent)"
Write-Host ""

Write-Host "==========================================" -ForegroundColor Green
Write-Host " Remove Resources" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

$allIotHubs = @($IotHubName) + $AdditionalIotHubNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

# Honour existing clean-start ordering first

# 1. Remove enrollment group from DPS
Remove-Quiet {
    az iot dps enrollment-group delete `
      --dps-name $DpsName `
      --enrollment-id $EnrollmentGroupName `
      --resource-group $ResourceGroup
} "[1/11] Removing enrollment group from DPS..."

# 2. Remove device from IoT Hub(s) (best effort pre-delete cleanup)
foreach ($hubName in $allIotHubs) {
        Remove-Quiet {
                az iot hub device-identity delete `
                    --hub-name $hubName `
                    --device-id $DeviceId
        } "[2/11] Removing device from IoT Hub '$hubName'..."
}

# 3. Remove device from ADR
if (Supports-AdrDelete) {
    Remove-Quiet {
        az iot dps namespace device delete `
            --dps-name $DpsName `
            --namespace-name $AdrNamespace `
            --device-id $DeviceId `
            --resource-group $ResourceGroup
    } "[3/11] Removing device from ADR (if supported)..."
} else {
    Write-Host "[3/11] Removing device from ADR (if supported)..." -ForegroundColor Cyan
    Write-Host "  -> SKIP (azure-iot extension version does not support ADR delete command)" -ForegroundColor Yellow
}

# 4. Remove CA certificates from DPS
Remove-DpsCert -CertName $RootCertName
Remove-DpsCert -CertName $IntermediateCertName

# 5. Clean local certificates and CSR files
$certsPath = Join-Path $PSScriptRoot "certs"
Remove-Quiet {
    Remove-Item -Path $certsPath -Recurse -Force -ErrorAction SilentlyContinue
} "[5/11] Cleaning local certificates..."

# Then remove remaining Azure resources

# 6. Delete all IoT Hubs first (ADR dependency)
foreach ($hubName in $allIotHubs) {
    Remove-Quiet {
        az iot hub delete `
            --name $hubName `
            --resource-group $ResourceGroup
    } "[6/11] Deleting IoT Hub '$hubName'..."
}

# 7. Unlink IoT Hub(s) from DPS (best effort; may already be removed after hub delete)
foreach ($hubName in $allIotHubs) {
    Remove-Quiet {
        az iot dps linked-hub delete `
            --dps-name $DpsName `
            --resource-group $ResourceGroup `
            --linked-hub $hubName
    } "[7/11] Unlinking IoT Hub '$hubName' from DPS..."
}

# 8. Delete DPS
Remove-Quiet {
    az iot dps delete `
        --name $DpsName `
    --resource-group $ResourceGroup
} "[8/11] Deleting DPS..."

# 9. Delete ADR namespace (after linked IoT Hub + DPS are deleted)
Remove-Quiet {
    az iot adr ns delete `
        --name $AdrNamespace `
    --resource-group $ResourceGroup `
    --yes
} "[9/11] Deleting ADR namespace..."

# 10. Delete user-assigned managed identity
Remove-Quiet {
    az identity delete `
        --name $UserIdentity `
        --resource-group $ResourceGroup
} "[10/11] Deleting user-assigned managed identity..."

# 11. Delete resource group (optional, default on)
if ($DeleteResourceGroup) {
    if ($NoWaitForResourceGroupDeletion.IsPresent) {
        Remove-Quiet {
            az group delete `
                --name $ResourceGroup `
                --yes `
                --no-wait
        } "[11/11] Deleting resource group (no wait)..."
    } else {
        Remove-Quiet {
            az group delete `
                --name $ResourceGroup `
                --yes
        } "[11/11] Deleting resource group..."
    }
} else {
    Write-Host "[11/11] Deleting resource group..." -ForegroundColor Cyan
    Write-Host "  -> SKIP (DeleteResourceGroup = false)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✅ Resource removal attempted." -ForegroundColor Green
Write-Host "If any step failed, scroll up for RED 'FAILED' lines and rerun as needed." -ForegroundColor Gray
