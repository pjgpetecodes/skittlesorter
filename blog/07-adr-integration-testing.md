# ADR Integration, Testing, and Troubleshooting

[← Previous: Building the Device Application](06-building-device-application.md) | [Back to Introduction](00-introduction.md)

---

Welcome to the final post in this series! We'll explore Azure Device Registry (ADR) integration, test the complete solution, and troubleshoot common issues.

## What is Azure Device Registry (ADR)?

Azure Device Registry is a **device identity and metadata management layer** that works with DPS and IoT Hub:

### Key Features

**🔹 Device Metadata**
- Attributes (hardware ID, model, version)
- Tags (location, tenant, environment)
- Custom properties

**🔹 Credential Management**
- Credential policies for certificate issuance
- Automated lifecycle management
- Certificate rotation

**🔹 Multi-Hub Support**
- Device identity independent of IoT Hub
- Move devices between hubs without re-provisioning
- Query devices across multiple hubs

**🔹 Programmatic Access**
- REST API for device operations
- Query devices by tags/attributes
- Update device metadata in real-time

## ADR Client Implementation

The project includes an ADR client for device management:

```csharp
using Azure.Identity;
using AzureDpsFramework.Adr;

// Create ADR client (uses DefaultAzureCredential)
var adrClient = new AdrDeviceRegistryClient();

// List all devices in namespace
var devices = await adrClient.ListDevicesAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "dev001-skittlesorter-rg",
    namespaceName: "dev001-skittlesorter-adr"
);

Console.WriteLine($"Found {devices.Count} devices:");
foreach (var device in devices)
{
    Console.WriteLine($"  - {device.Name} ({device.Properties?.Enabled ?? false})");
}

// Get specific device
var deviceDetails = await adrClient.GetDeviceAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "dev001-skittlesorter-rg",
    namespaceName: "dev001-skittlesorter-adr",
    deviceName: "dev001-skittlesorter"
);

if (deviceDetails != null)
{
    Console.WriteLine($"\nDevice: {deviceDetails.Name}");
    Console.WriteLine($"Enabled: {deviceDetails.Properties?.Enabled}");
    Console.WriteLine($"Hardware ID: {deviceDetails.Properties?.Attributes?.HardwareId}");
    Console.WriteLine($"OS Version: {deviceDetails.Properties?.Attributes?.OperatingSystemVersion}");
    
    // Display tags
    if (deviceDetails.Properties?.Tags != null)
    {
        Console.WriteLine("Tags:");
        foreach (var tag in deviceDetails.Properties.Tags)
        {
            Console.WriteLine($"  {tag.Key}: {tag.Value}");
        }
    }
}
```

### Device Model

```csharp
public class DeviceResource
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public DeviceProperties? Properties { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class DeviceProperties
{
    public bool Enabled { get; set; }
    public DeviceAttributes? Attributes { get; set; }
    public Dictionary<string, object>? Tags { get; set; }
}

public class DeviceAttributes
{
    public string? HardwareId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? OperatingSystemVersion { get; set; }
}
```

## Updating Device Metadata

Update device attributes after provisioning:

```csharp
// Update device properties
var updateRequest = new DeviceUpdateRequest
{
    Properties = new DevicePropertiesUpdate
    {
        Enabled = true,
        Attributes = new DeviceAttributesUpdate
        {
            OperatingSystemVersion = "Linux 6.1.0",
            Manufacturer = "Raspberry Pi Foundation",
            Model = "Raspberry Pi 4B"
        },
        Tags = new Dictionary<string, object>
        {
            { "location", "factory-1" },
            { "environment", "production" },
            { "firmware", "1.0.5" }
        }
    }
};

await adrClient.UpdateDeviceAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "dev001-skittlesorter-rg",
    namespaceName: "dev001-skittlesorter-adr",
    deviceName: "dev001-skittlesorter",
    update: updateRequest
);

Console.WriteLine("Device metadata updated successfully.");
```

## Configuration-Driven Updates

The application supports automatic ADR updates via configuration:

```json
{
  "Adr": {
    "SubscriptionId": "your-subscription-id",
    "ResourceGroup": "dev001-skittlesorter-rg",
    "Namespace": "dev001-skittlesorter-adr",
    "DeviceUpdate": {
      "Enabled": true,
      "Attributes": {
        "HardwareId": "RPI4B-12345678",
        "Manufacturer": "Raspberry Pi Foundation",
        "Model": "Raspberry Pi 4B",
        "OperatingSystemVersion": "Raspbian GNU/Linux 11"
      },
      "Tags": {
        "location": "factory-1",
        "department": "manufacturing",
        "firmware": "1.0.5",
        "lastDeployment": "2026-01-25"
      }
    }
  }
}
```

Automatic update on startup:

```csharp
// In DpsInitializationService.cs
private static async Task UpdateDeviceInAdrAsync(
    DeviceRegistrationResult result,
    DpsConfiguration dpsCfg)
{
    var adrConfig = AdrConfiguration.Load();
    if (!adrConfig.DeviceUpdate?.Enabled ?? false)
    {
        Console.WriteLine("ADR device update disabled in configuration.");
        return;
    }
    
    Console.WriteLine("\n=== Updating Device in ADR ===");
    
    var adrClient = new AdrDeviceRegistryClient();
    
    // Update device metadata
    await adrClient.UpdateDeviceAsync(
        subscriptionId: adrConfig.SubscriptionId,
        resourceGroupName: adrConfig.ResourceGroup,
        namespaceName: adrConfig.Namespace,
        deviceName: result.DeviceId,
        update: new DeviceUpdateRequest
        {
            Properties = new DevicePropertiesUpdate
            {
                Enabled = true,
                Attributes = adrConfig.DeviceUpdate.Attributes,
                Tags = adrConfig.DeviceUpdate.Tags
            }
        }
    );
    
    Console.WriteLine("✓ Device metadata updated in ADR");
}
```

## Testing the Complete Solution

### Test 1: Initial Provisioning

```powershell
# Run the device application
dotnet run

# Expected output:
# Skittle Sorter Starting...
# 
# === DPS Configuration ===
# IdScope: 0ne00XXXXXX
# RegistrationId: dev001-skittlesorter
# AttestationMethod: SymmetricKey
# 
# === Starting DPS Provisioning ===
# Device key derived from enrollment group key.
# Generated RSA key pair (2048-bit) for CSR.
# Submitting registration request to DPS...
# ✓ Device assigned to: dev001-skittlesorter-hub.azure-devices.net
# ✓ Device ID: dev001-skittlesorter
# 
# === Saving Issued Certificate ===
# Certificate Subject: CN=dev001-skittlesorter
# Valid until: 2026-02-24 12:34:56
# ✓ Certificate saved to: ./certs/issued/device.pfx
# 
# === Connecting to IoT Hub ===
# ✓ Connected to IoT Hub
# 
# === Starting Sorting Loop ===
```

### Test 2: Verify Device in IoT Hub

```powershell
# Check device exists in IoT Hub
az iot hub device-identity show `
  --hub-name dev001-skittlesorter-hub `
  --device-id dev001-skittlesorter

# Expected output:
# {
#   "deviceId": "dev001-skittlesorter",
#   "authentication": {
#     "type": "certificateAuthority"
#   },
#   "connectionState": "Connected",
#   "status": "enabled"
# }
```

### Test 3: Monitor Telemetry

```powershell
# Monitor device-to-cloud messages
az iot hub monitor-events `
  --hub-name dev001-skittlesorter-hub `
  --device-id dev001-skittlesorter

# Expected output:
# Starting event monitor...
# {
#   "event": {
#     "origin": "dev001-skittlesorter",
#     "payload": {
#       "deviceId": "dev001-skittlesorter",
#       "timestamp": "2026-01-25T12:34:56Z",
#       "color": "red",
#       "count": 1
#     }
#   }
# }
```

### Test 4: Query ADR Devices

```powershell
# List devices using Azure CLI
az iot ops asset endpoint device list `
  --namespace dev001-skittlesorter-adr `
  --resource-group dev001-skittlesorter-rg `
  --output table

# Expected output:
# Name                    Enabled    HardwareId         Location
# ----------------------  ---------  -----------------  ----------
# dev001-skittlesorter    True       RPI4B-12345678     factory-1
```

### Test 5: Device Twin Operations

```powershell
# Update device twin desired properties
az iot hub device-twin update `
  --hub-name dev001-skittlesorter-hub `
  --device-id dev001-skittlesorter `
  --set properties.desired='{"telemetryInterval":3000,"enableSorting":true}'

# Device should receive update and respond:
# === Device Twin Update Received ===
# {
#   "telemetryInterval": 3000,
#   "enableSorting": true
# }
# Reported properties updated.
```

## Troubleshooting Guide

### Issue 1: DPS Registration Fails

**Symptoms:**
```
Failed to connect to DPS: Authentication failed
```

**Causes:**
- Incorrect ID Scope
- Wrong enrollment group key
- Device key derivation error
- Enrollment group disabled

**Solutions:**

```powershell
# Verify DPS ID Scope
az iot dps show `
  --name dev001-skittlesorter-dps `
  --resource-group dev001-skittlesorter-rg `
  --query properties.idScope -o tsv

# Check enrollment group status
az iot dps enrollment-group show `
  --dps-name dev001-skittlesorter-dps `
  --resource-group dev001-skittlesorter-rg `
  --enrollment-id dev001-skittlesorter-group `
  --query provisioningStatus

# Verify device key derivation
$enrollmentKey = "your-enrollment-key"
$registrationId = "dev001-skittlesorter"

$keyBytes = [Convert]::FromBase64String($enrollmentKey)
$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = $keyBytes
$deviceKeyBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($registrationId))
$deviceKey = [Convert]::ToBase64String($deviceKeyBytes)

Write-Host "Derived Device Key: $deviceKey"
```

### Issue 2: Certificate Not Issued

**Symptoms:**
```
Registration successful but IssuedCertificateChain is null
```

**Causes:**
- Credential policy not linked to enrollment group
- CSR not included in registration request
- ADR namespace misconfigured

**Solutions:**

```powershell
# Verify credential policy exists
az iot hub device-identity credential-policy list `
  --namespace dev001-skittlesorter-adr `
  --resource-group dev001-skittlesorter-rg

# Check enrollment group has credential policy
az iot dps enrollment-group show `
  --dps-name dev001-skittlesorter-dps `
  --resource-group dev001-skittlesorter-rg `
  --enrollment-id dev001-skittlesorter-group `
  --query credentialPolicy

# Verify ADR namespace linked to DPS
az iot dps show `
  --name dev001-skittlesorter-dps `
  --resource-group dev001-skittlesorter-rg `
  --query properties.deviceRegistry
```

### Issue 3: IoT Hub Connection Fails

**Symptoms:**
```
Failed to connect to IoT Hub: 401 Unauthorized
```

**Causes:**
- Certificate not installed correctly
- Private key missing
- Certificate expired
- Device not registered in IoT Hub

**Solutions:**

```csharp
// Verify certificate has private key
var cert = new X509Certificate2("device-cert.pfx", "password");
if (!cert.HasPrivateKey)
{
    Console.WriteLine("ERROR: Certificate missing private key");
}

// Check certificate validity
Console.WriteLine($"Valid from: {cert.NotBefore}");
Console.WriteLine($"Valid until: {cert.NotAfter}");

if (DateTime.UtcNow > cert.NotAfter)
{
    Console.WriteLine("ERROR: Certificate expired");
}

// Verify device exists in IoT Hub
// az iot hub device-identity show --hub-name ... --device-id ...
```

### Issue 4: ADR Access Denied

**Symptoms:**
```
HTTP 403: Insufficient permissions to access ADR namespace
```

**Causes:**
- Missing role assignments
- Managed identity not configured
- Incorrect Azure credentials

**Solutions:**

```powershell
# Check role assignments for user
az role assignment list `
  --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.DeviceRegistry/namespaces/$adrNamespace" `
  --assignee your-user@domain.com `
  --output table

# Assign Device Registry Contributor role
az role assignment create `
  --assignee your-user@domain.com `
  --role "Device Registry Contributor" `
  --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.DeviceRegistry/namespaces/$adrNamespace"

# For application, use managed identity or service principal
# Login with: az login --service-principal --username APP_ID --password PASSWORD --tenant TENANT_ID
```

### Issue 5: Certificate Renewal Not Working

**Symptoms:**
```
Certificate expired, device cannot connect
```

**Solutions:**

Implement automatic renewal:

```csharp
// Check certificate expiration daily
var timer = new System.Timers.Timer(TimeSpan.FromDays(1).TotalMilliseconds);
timer.Elapsed += async (sender, e) =>
{
    await CheckAndRenewCertificateAsync();
};
timer.Start();

async Task CheckAndRenewCertificateAsync()
{
    var cert = new X509Certificate2(certPath, password);
    var renewalWindow = cert.NotAfter.AddDays(-7);
    
    if (DateTime.UtcNow >= renewalWindow)
    {
        Console.WriteLine("Certificate renewal needed. Re-provisioning...");
        
        // Delete old certificate
        File.Delete(certPath);
        
        // Re-provision to get new certificate
        var deviceClient = DpsInitializationService.Initialize(iotConfig);
        
        // Reconnect
        await deviceClient.OpenAsync();
    }
}
```

## Production Deployment Checklist

### ✅ Security

- [ ] Store enrollment keys in Azure Key Vault
- [ ] Use hardware security modules (TPM) for private keys
- [ ] Enable certificate pinning
- [ ] Implement certificate revocation checks
- [ ] Use separate enrollment groups for dev/staging/prod
- [ ] Rotate enrollment group keys periodically

### ✅ Reliability

- [ ] Implement exponential backoff for retries
- [ ] Handle network disconnections gracefully
- [ ] Monitor certificate expiration
- [ ] Implement automatic certificate renewal
- [ ] Log all provisioning events
- [ ] Set up alerting for failed provisioning

### ✅ Monitoring

- [ ] Enable Azure Monitor for IoT Hub
- [ ] Configure diagnostic logs for DPS
- [ ] Track device connection metrics
- [ ] Monitor telemetry delivery rates
- [ ] Set up alerts for certificate expiration
- [ ] Track ADR API usage and errors

### ✅ Configuration

- [ ] Use environment-specific appsettings.json files
- [ ] Store secrets in Key Vault or environment variables
- [ ] Document all configuration parameters
- [ ] Validate configuration on startup
- [ ] Implement configuration hot-reload

### ✅ Testing

- [ ] Unit tests for key derivation
- [ ] Integration tests for DPS provisioning
- [ ] E2E tests for telemetry flow
- [ ] Load testing for concurrent provisioning
- [ ] Certificate renewal testing

## Clean Test Start Script

The project includes a PowerShell script for testing:

```powershell
# scripts/clean-test-start.ps1

Write-Host "=== Clean Test Start ===" -ForegroundColor Yellow

# 1. Stop running processes
Write-Host "Stopping existing processes..."
Get-Process -Name "skittlesorter" -ErrorAction SilentlyContinue | Stop-Process

# 2. Delete issued certificates
Write-Host "Cleaning certificates..."
Remove-Item "./certs/issued/*" -Force -ErrorAction SilentlyContinue

# 3. Delete device from IoT Hub
Write-Host "Removing device from IoT Hub..."
az iot hub device-identity delete `
  --hub-name dev001-skittlesorter-hub `
  --device-id dev001-skittlesorter `
  --resource-group dev001-skittlesorter-rg `
  --output none `
  --only-show-errors

# 4. Delete device from ADR
Write-Host "Removing device from ADR..."
az iot ops asset endpoint device delete `
  --namespace dev001-skittlesorter-adr `
  --resource-group dev001-skittlesorter-rg `
  --device-name dev001-skittlesorter `
  --yes `
  --output none `
  --only-show-errors

# 5. Wait for propagation
Write-Host "Waiting for changes to propagate..."
Start-Sleep -Seconds 5

# 6. Run application
Write-Host "Starting application..." -ForegroundColor Green
dotnet run

Write-Host "`n=== Clean Test Complete ===" -ForegroundColor Yellow
```

## Next Steps

Congratulations! You've completed the series. You now have:

✅ **Understanding of DPS** and CSR-based certificate issuance  
✅ **Azure infrastructure** (DPS, IoT Hub, ADR)  
✅ **Custom DPS framework** with preview API support  
✅ **Complete device application** with provisioning and telemetry  
✅ **ADR integration** for device management  
✅ **Testing and troubleshooting** knowledge  

### Further Learning

- **Azure IoT Hub Documentation**: [learn.microsoft.com/azure/iot-hub](https://learn.microsoft.com/azure/iot-hub/)
- **Device Provisioning Service**: [learn.microsoft.com/azure/iot-dps](https://learn.microsoft.com/azure/iot-dps/)
- **Azure Device Registry**: [learn.microsoft.com/azure/iot/iot-device-registry-overview](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
- **X.509 Certificates**: [learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview](https://learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview)

### Repository

Clone the complete project:
```bash
git clone https://github.com/yourusername/skittlesorter.git
cd skittlesorter
```

### Questions or Issues?

- Open an issue on GitHub
- Check the [Troubleshooting.md](../docs/Troubleshooting.md) guide
- Review [Azure Setup documentation](../docs/Azure-Setup.md)

Thank you for following this series! Happy IoT building! 🚀

---

[← Back to Introduction](00-introduction.md) | [View on GitHub](https://github.com/yourusername/skittlesorter)
