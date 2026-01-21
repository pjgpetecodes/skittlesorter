param(
    [Parameter(Mandatory=$true)][string]$DpsName,
    [Parameter(Mandatory=$true)][string]$IotHubName,
    [Parameter(Mandatory=$true)][string]$ResourceGroup,
    [Parameter(Mandatory=$true)][string]$EnrollmentGroupName,
    [Parameter(Mandatory=$true)][string]$DeviceId,
    [Parameter(Mandatory=$false)][string]$AdrNamespace,
    [switch]$RemoveCaCerts
)

function Remove-Quiet {
    param(
        [scriptblock]$Action,
        [string]$Description
    )
    try {
        Write-Host $Description -ForegroundColor Cyan
        & $Action 2>$null | Out-Null
    } catch {
        Write-Host "(ignored) $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host " Clean Test Start" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

# 1. Remove enrollment group from DPS
Remove-Quiet {
    az iot dps enrollment-group delete `
      --dps-name $DpsName `
      --enrollment-id $EnrollmentGroupName `
      --resource-group $ResourceGroup `
      --yes
} "Removing enrollment group from DPS..."

# 2. Remove device from IoT Hub
Remove-Quiet {
    az iot hub device-identity delete `
      --hub-name $IotHubName `
      --device-id $DeviceId `
      --yes
} "Removing device from IoT Hub..."

# 3. Remove device from ADR (optional if namespace provided)
if ($AdrNamespace) {
    Remove-Quiet {
        az iot dps namespace device delete `
          --dps-name $DpsName `
          --namespace-name $AdrNamespace `
          --device-id $DeviceId `
          --resource-group $ResourceGroup `
          --yes
    } "Removing device from ADR..."
}

# 4. Optional: Remove CA certificates from DPS
if ($RemoveCaCerts.IsPresent) {
    Remove-Quiet {
        az iot dps ca-certificate delete `
          --dps-name $DpsName `
          --cert-name root-ca `
          --resource-group $ResourceGroup `
          --yes
    } "Removing DPS CA certificate: root-ca..."

    Remove-Quiet {
        az iot dps ca-certificate delete `
          --dps-name $DpsName `
          --cert-name intermediate-ca `
          --resource-group $ResourceGroup `
          --yes
    } "Removing DPS CA certificate: intermediate-ca..."
}

# 5. Clean local certificates and CSR files
Remove-Quiet {
    Remove-Item -Path certs -Recurse -Force -ErrorAction SilentlyContinue
} "Cleaning local certificates..."

Write-Host ""
Write-Host "✅ Clean test reset complete." -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Gray
Write-Host "1) Re-run setup: pwsh ./scripts/setup-x509-attestation.ps1 ..." -ForegroundColor Gray
Write-Host "2) Run app: dotnet run" -ForegroundColor Gray
