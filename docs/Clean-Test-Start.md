# Clean Test Start

**Purpose:** Reset device artifacts created by the setup script and application, so you can run through the provisioning flow again from scratch.

Use this guide when you want to:
- Test provisioning multiple times without rebuilding Azure infrastructure
- Verify the setup script creates resources correctly
- Test enrollment group and device creation again
- See all the steps (certificate generation, DPS registration, IoT Hub connection) happen fresh

This cleans **only what the setup and app create** (enrollment groups, devices, local certificates) — it keeps your underlying Azure resources (DPS, IoT Hub, ADR namespace) intact unless you optionally remove the uploaded CA certificates.

## What Gets Cleaned

✅ **Local artifacts** (certificates and CSR files in `certs/` directory)
✅ **Enrollment groups** created by setup script
✅ **Devices** registered in IoT Hub
✅ **Devices** registered in ADR

❌ **NOT removed**: DPS instance, IoT Hub instance, ADR namespace
⚠️ **Optional**: Delete DPS CA certificates (root + intermediate) if you want brand-new certs for the next run

This allows you to re-run `setup-x509-attestation.ps1` and the application to create them again for testing.

## Prerequisites

Ensure you have the Azure CLI installed with the IoT extension:
```powershell
az extension add --name azure-iot
```

## Quick Cleanup Script

You can run the helper script from the repo root:

```powershell
pwsh ./scripts/clean-test-start.ps1 `
  -DpsName "your-dps" `
  -IotHubName "your-hub" `
  -ResourceGroup "your-rg" `
  -EnrollmentGroupName "your-eg" `
  -DeviceId "skittlesorter" `
  -AdrNamespace "your-adr-namespace" `       # optional; omit if not using ADR
  -RootCertName "skittlesorter-root" `       # optional; defaults to root-ca
  -IntermediateCertName "skittlesorter-intermediate" ` # optional; defaults to intermediate-ca
  -RemoveCaCerts                              # optional; include to delete DPS root/intermediate CAs
```

Run this to reset everything for a clean re-test:

```powershell
# Variables (adjust to match your setup)
$dpsName = "your-dps-name"
$iotHubName = "your-iothub-name"
$resourceGroup = "your-resource-group"
$enrollmentGroupName = "your-enrollment-group"
$deviceId = "skittlesorter"
$adrNamespace = "your-adr-namespace"

# 1. Remove enrollment group from DPS
Write-Host "Removing enrollment group from DPS..."
az iot dps enrollment-group delete `
  --dps-name $dpsName `
  --enrollment-id $enrollmentGroupName `
  --resource-group $resourceGroup `
  --yes 2>$null

# 2. Remove device from IoT Hub
Write-Host "Removing device from IoT Hub..."
az iot hub device-identity delete `
  --hub-name $iotHubName `
  --device-id $deviceId `
  --yes 2>$null

# 3. Remove device from ADR (if enabled)
Write-Host "Removing device from ADR..."
az iot dps namespace device delete `
  --dps-name $dpsName `
  --namespace-name $adrNamespace `
  --device-id $deviceId `
  --resource-group $resourceGroup `
  --yes 2>$null

# 4. OPTIONAL: Remove CA certificates from DPS for a truly fresh run
# (only if you want the setup script to upload brand-new root/intermediate)
Write-Host "Removing CA certificates from DPS (optional)..."
az iot dps ca-certificate delete `
  --dps-name $dpsName `
  --cert-name root-ca `
  --resource-group $resourceGroup `
  --yes 2>$null

az iot dps ca-certificate delete `
  --dps-name $dpsName `
  --cert-name intermediate-ca `
  --resource-group $resourceGroup `
  --yes 2>$null

# 5. Clean local certificates and CSR files
Write-Host "Cleaning local certificates..."
Remove-Item -Path certs -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=========================================="
Write-Host "✅ Clean test reset complete!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Next steps:"
Write-Host "1. Re-run setup: pwsh ./scripts/setup-x509-attestation.ps1"
Write-Host "2. Run app: dotnet run"
Write-Host "3. Watch enrollment group and device get created again"
```

## Manual Cleanup Steps

If you prefer to do this manually or selectively:

### Remove Enrollment Group

```powershell
az iot dps enrollment-group delete `
  --dps-name $dpsName `
  --enrollment-id $enrollmentGroupName `
  --resource-group $resourceGroup
```

### Remove Device from IoT Hub

```powershell
az iot hub device-identity delete `
  --hub-name $iotHubName `
  --device-id $deviceId
```

### Remove Device from ADR

```powershell
az iot dps namespace device delete `
  --dps-name $dpsName `
  --namespace-name $adrNamespace `
  --device-id $deviceId `
  --resource-group $resourceGroup
```

### Remove Local Certificates

```powershell
# Backup first (optional)
Copy-Item -Path certs -Destination certs.backup -Recurse -Force

# Clean up
Remove-Item -Path certs -Recurse -Force
```

## Testing Workflow

After cleanup, you can re-test the full provisioning flow:

1. **Run setup script** (creates enrollment group and CA certificate uploads):
   ```powershell
   pwsh ./scripts/setup-x509-attestation.ps1 `
     -RegistrationId skittlesorter `
     -EnrollmentGroupId skittlesorter-group `
     -DpsName $dpsName `
     -ResourceGroup $resourceGroup `
     -CredentialPolicy $credentialPolicyName
   ```

2. **Run application** (provisions device to DPS, gets certificate, connects to IoT Hub):
   ```powershell
   dotnet run
   ```

3. **Verify** in Azure:
   - Check DPS for enrolled device
   - Check IoT Hub for connected device
   - Check ADR for device record (if enabled)

4. **Cleanup and repeat** using the script above

## Troubleshooting

**"Enrollment group not found" error?**
- The enrollment group may have already been deleted
- Check the group name matches your configuration

**Device still in IoT Hub after deletion?**
- Make sure the device ID is correct
- Device might be in a different hub

**Certificates still in certs/ folder after running cleanup?**
- The script deletes the directory; if it fails, try: `Remove-Item -Path certs -Recurse -Force -ErrorAction SilentlyContinue`

**Want to keep CA certificates but just reset the device?**
- Skip step 3 (removing from ADR) in the cleanup script
- You can still delete the enrollment group and device, then re-create them with `setup-x509-attestation.ps1`

## Related

- [Azure Setup](./Azure-Setup.md) - Initial Azure resource setup
- [DPS Provisioning](./DPS-Provisioning.md) - DPS provisioning flows
- [Configuration](./Configuration.md) - Configuration reference
- [Quickstart](./Quickstart.md) - Getting started guide
