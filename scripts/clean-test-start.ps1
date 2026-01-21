param(
    [Parameter(Mandatory=$true)][string]$DpsName,
    [Parameter(Mandatory=$true)][string]$IotHubName,
    [Parameter(Mandatory=$true)][string]$ResourceGroup,
    [Parameter(Mandatory=$true)][string]$EnrollmentGroupName,
    [Parameter(Mandatory=$true)][string]$DeviceId,
    [Parameter(Mandatory=$false)][string]$AdrNamespace,
    [switch]$RemoveCaCerts,
    [Parameter(Mandatory=$false)][string]$RootCertName = "root-ca",
    [Parameter(Mandatory=$false)][string]$IntermediateCertName = "intermediate-ca"
)

function Remove-Quiet {
    param(
        [scriptblock]$Action,
        [string]$Description
    )
    try {
        Write-Host $Description -ForegroundColor Cyan
        & $Action | Out-Null
        if ($LASTEXITCODE -ne 0) {
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
Write-Host "  DPS:              $DpsName"
Write-Host "  IoT Hub:          $IotHubName"
Write-Host "  Resource Group:   $ResourceGroup"
Write-Host "  Enrollment Group: $EnrollmentGroupName"
Write-Host "  DeviceId:         $DeviceId"
if ($AdrNamespace) { Write-Host "  ADR Namespace:   $AdrNamespace" }
Write-Host "  Remove CA Certs:  $($RemoveCaCerts.IsPresent)"
if ($RemoveCaCerts.IsPresent) {
    Write-Host "  Root Cert Name:  $RootCertName"
    Write-Host "  Int Cert Name:   $IntermediateCertName"
}
Write-Host ""

Write-Host "==========================================" -ForegroundColor Green
Write-Host " Clean Test Start" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

# 1. Remove enrollment group from DPS
Remove-Quiet {
    az iot dps enrollment-group delete `
      --dps-name $DpsName `
    --enrollment-id $EnrollmentGroupName `
    --resource-group $ResourceGroup
} "Removing enrollment group from DPS..."

# 2. Remove device from IoT Hub
Remove-Quiet {
    az iot hub device-identity delete `
      --hub-name $IotHubName `
    --device-id $DeviceId
} "Removing device from IoT Hub..."

# 3. Remove device from ADR (optional if namespace provided)
if ($AdrNamespace) {
    if (Supports-AdrDelete) {
        Remove-Quiet {
            az iot dps namespace device delete `
                --dps-name $DpsName `
                --namespace-name $AdrNamespace `
                --device-id $DeviceId `
                --resource-group $ResourceGroup
        } "Removing device from ADR (if supported)..."
    } else {
        Write-Host "Removing device from ADR (if supported)..." -ForegroundColor Cyan
        Write-Host "  -> SKIP (azure-iot extension version does not support ADR delete command)" -ForegroundColor Yellow
    }
}

# 4. Optional: Remove CA certificates from DPS
if ($RemoveCaCerts.IsPresent) {
    Remove-DpsCert -CertName $RootCertName
    Remove-DpsCert -CertName $IntermediateCertName
}

# 5. Clean local certificates and CSR files
Remove-Quiet {
    Remove-Item -Path certs -Recurse -Force -ErrorAction SilentlyContinue
} "Cleaning local certificates..."

Write-Host ""
Write-Host "✅ Clean test reset attempted." -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Gray
Write-Host "1) Re-run setup: pwsh ./scripts/setup-x509-attestation.ps1 ..." -ForegroundColor Gray
Write-Host "2) Run app: dotnet run" -ForegroundColor Gray
Write-Host "3) If something failed above, scroll up for RED 'FAILED' lines." -ForegroundColor Gray
