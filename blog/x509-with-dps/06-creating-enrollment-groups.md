# Creating X.509 Enrollment Groups

[← Previous: Verifying X.509 Certificates](05-verifying-x509-certificates.md) | [Next: Setting up a Simulated Device (C#) →](07a-setting-up-csharp-device.md)

---

## Enrollment Groups: Authorizing Devices at Scale

Your certificates are verified and DPS is ready. Now we configure which devices are allowed to provision. We'll use an enrollment group — a single policy that authorizes all devices with certificates signed by your CA.

## What is an Enrollment Group?

An **enrollment group** is a configuration in DPS that defines which devices are allowed to provision and where they should be assigned.

### Enrollment Group vs Individual Enrollment

| Feature | Enrollment Group | Individual Enrollment |
|---------|------------------|----------------------|
| Scope | Many devices | One device |
| Certificate | Shared CA | Unique per device |
| Scale | Thousands/millions | Manual, one-by-one |
| Management | Easy (one config) | Tedious at scale |
| Use Case | Production fleets | Special/critical devices |

**For this guide, we're using enrollment groups** - the scalable approach.

## How Enrollment Groups Work

```
┌────────────────────────────────────────┐
│     DPS Enrollment Group               │
│                                        │
│  Name: "my-device-group"              │
│  CA: my-intermediate-ca (verified)    │
│  Status: Enabled                      │
└────────────────────────────────────────┘
                   │
                   │ Any device with cert signed by
                   │ my-intermediate-ca can provision
                   │
        ┌──────────┴──────────┐
        │                     │
    ┌───▼────┐          ┌─────▼───┐
    │Device 1│          │Device 2 │
    │        │          │         │
    │Cert CN:│          │Cert CN: │
    │device-1│          │device-2 │
    └────────┘          └─────────┘
```

**Key insight:** You don't register individual devices. You register a CA, and any device with a certificate signed by that CA can provision.

## Prerequisites

- DPS instance with verified certificates
- Root CA verified ✅
- Intermediate CA verified ✅

```powershell
# Verify certificates are ready
az iot dps certificate list `
  --dps-name "my-dps-x509" `
  --resource-group "iot-x509-demo-rg" `
  --query "value[].{Name:name, Verified:properties.isVerified}" `
  -o table
```

Expected: Both should show `Verified: True`

## Creating an Enrollment Group

### Basic Enrollment Group

Now we create an enrollment group in DPS that links to our verified intermediate CA certificate.

```powershell
$dpsName = "my-dps-x509"
$resourceGroup = "iot-x509-demo-rg"

az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  --ca-name "my-intermediate-ca" `
  --provisioning-status enabled
```

**Parameters explained:**
- `--enrollment-id`: Unique name for this enrollment group
- `--ca-name`: Name of verified CA (use intermediate, not root)
- `--provisioning-status`: `enabled` or `disabled`

**Expected output:**
```json
{
  "allocationPolicy": "hashed",
  "attestation": {
    "type": "x509",
    "x509": {
      "caReferences": {
        "primary": "my-intermediate-ca"
      }
    }
  },
  "enrollmentGroupId": "my-device-group",
  "etag": "AAAAAAFPTRs=",
  "provisioningStatus": "enabled"
}
```

### Why Use Intermediate CA (Not Root)?

**Best Practice:** Use the intermediate CA in enrollment groups.

**Reasons:**
- 🔒 **Security**: Root key stays offline
- 🔄 **Rotation**: Can replace intermediate without touching root
- 🎯 **Scope**: Different intermediates can have different enrollment policies
- ⚖️ **Separation**: Root for trust, intermediate for operations

**Example:** Multiple enrollment groups:
```
Root CA (offline, secure)
├─ Intermediate-Production → enrollment-group-prod
├─ Intermediate-Testing → enrollment-group-test
└─ Intermediate-Development → enrollment-group-dev
```

## Allocation Policies

When a device provisions, DPS decides which IoT Hub to assign it to. The allocation policy controls this decision.

In this step we create an enrollment group with a specific allocation policy to control hub assignment.

```powershell
# Create enrollment with specific allocation policy
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  --ca-name "my-intermediate-ca" `
  --provisioning-status enabled `
  --allocation-policy "hashed"
```

### Available Policies

| Policy | Description | Use Case |
|--------|-------------|----------|
| `hashed` | Evenly distribute devices across hubs | Load balancing |
| `geolatency` | Assign to closest hub | Multi-region deployments |
| `static` | Always use same hub | Simple setups |
| `custom` | Azure Function decides | Complex business logic |

**Default:** `hashed` (evenly distributes devices)

## Configuring Initial Device Twin

Next we create a twin configuration file and include it in our enrollment group to set default properties for all provisioned devices.

```powershell
# Create twin configuration file
$twinConfig = @{
  tags = @{
    environment = "production"
    location = "factory-floor"
  }
  properties = @{
    desired = @{
      telemetryInterval = 60
      firmware = "1.0.0"
    }
  }
} | ConvertTo-Json -Depth 10

$twinConfig | Set-Content -Path "twin-config.json"

# Create enrollment with initial twin
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  --ca-name "my-intermediate-ca" `
  --provisioning-status enabled `
  --initial-twin-properties "twin-config.json"
```

**What this does:**
- All devices in this group start with these twin properties
- Useful for configuration management
- Can be overridden per device after provisioning

## View Enrollment Details

Now we retrieve the full enrollment group configuration to verify all settings are correct.

```powershell
az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group"
```

**Key properties to note:**
- `attestation.type`: Should be `x509`
- `attestation.x509.caReferences.primary`: Your intermediate CA name
- `provisioningStatus`: Should be `enabled`
- `allocationPolicy`: Distribution strategy

## List All Enrollment Groups

Next we list all enrollment groups to see an overview of all configured provisioning policies.

```powershell
az iot dps enrollment-group list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --query "[].{EnrollmentID:enrollmentGroupId, CA:attestation.x509.caReferences.primary, Status:provisioningStatus}" `
  -o table
```

**Output:**
```
EnrollmentID      CA                   Status
----------------  -------------------  --------
my-device-group   my-intermediate-ca   enabled
```

## Update Enrollment Group

Need to change settings? Update the enrollment:

In this step we modify existing enrollment group settings such as provisioning status or allocation policy.

```powershell
# Disable provisioning temporarily
az iot dps enrollment-group update `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  --provisioning-status disabled

# Change allocation policy
az iot dps enrollment-group update `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  --allocation-policy "geolatency"
```

## Verify in Azure Portal

1. Navigate to your DPS instance
2. Click **Manage enrollments**
3. You should see your enrollment group listed

**Click on the enrollment to view:**
- Attestation mechanism: X.509
- Primary certificate: my-intermediate-ca ✅
- Provisioning status: Enabled
- Allocation policy: Hashed
- Registration records (populated after devices provision)

## Multiple Enrollment Groups

You can create multiple enrollment groups for different device types or tenants:

```powershell
# Sensors
az iot dps enrollment-group create `
  --enrollment-id "sensor-devices" `
  --ca-name "my-intermediate-ca" `
  ... 

# Gateways
az iot dps enrollment-group create `
  --enrollment-id "gateway-devices" `
  --ca-name "my-intermediate-ca" `
  ...

# Customer A devices
az iot dps enrollment-group create `
  --enrollment-id "customer-a-devices" `
  --ca-name "customer-a-intermediate-ca" `
  ...
```

**How devices choose enrollment:**
- Devices present certificates during provisioning
- DPS matches certificate to CA
- CA determines which enrollment group applies
- Multiple groups can share the same CA

## Registration Records

After devices provision, you can see registration history:

```powershell
# This will be empty until devices provision
az iot dps enrollment-group registration list `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group"
```

**Returns:** List of devices that have provisioned through this enrollment group.

## Security Considerations

### Enrollment Group Access Control

**Who can provision?**
- Any device with a certificate signed by the enrolled CA
- Certificate must be valid (not expired)
- Certificate CN becomes the device registration ID

**To revoke access:**
```powershell
# Option 1: Disable entire enrollment group
az iot dps enrollment-group update --provisioning-status disabled

# Option 2: Create individual enrollment with status disabled (blacklist)
az iot dps enrollment create --enrollment-id "device-001" --attestation-type x509 --provisioning-status disabled
```

### Certificate Validation

DPS performs these checks:
1. ✅ Certificate signature is valid
2. ✅ Certificate chains to verified CA
3. ✅ Certificate is not expired
4. ✅ CA certificate is verified in DPS
5. ✅ Enrollment group is enabled

## Common Errors

### Error: "Certificate not found"

```
Error: Certificate 'my-intermediate-ca' not found
```

**Solution:** Verify certificate name exactly matches:
```powershell
az iot dps certificate list --dps-name $dpsName --resource-group $resourceGroup
```

### Error: "Certificate not verified"

```
Error: Certificate must be verified before use in enrollment
```

**Solution:** Complete verification process from previous section.

### Error: "Enrollment already exists"

```
Error: Enrollment group 'my-device-group' already exists
```

**Solution:** Use a different name or update existing:
```powershell
az iot dps enrollment-group update ...
```

## Testing the Enrollment

Before deploying devices, verify the enrollment is configured correctly:

```powershell
# Get enrollment details
$enrollment = az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id "my-device-group" `
  -o json | ConvertFrom-Json

# Verify configuration
Write-Host "Enrollment ID: $($enrollment.enrollmentGroupId)"
Write-Host "CA Reference: $($enrollment.attestation.x509.caReferences.primary)"
Write-Host "Status: $($enrollment.provisioningStatus)"
Write-Host "Allocation Policy: $($enrollment.allocationPolicy)"
```

**Checklist:**
- ✅ CA reference matches your intermediate CA name
- ✅ Status is "enabled"
- ✅ Allocation policy is appropriate for your setup

## What We've Accomplished

✅ Created enrollment group in DPS  
✅ Linked to verified intermediate CA  
✅ Configured allocation policy  
✅ Enabled provisioning  
✅ Ready to accept device registrations  

**Key Takeaway:** One enrollment group can support thousands of devices. As long as device certificates are signed by the enrolled CA, they can provision automatically.

## Next Steps

Now we'll create a simulated IoT device that uses our device certificate to provision through DPS!

---

[← Previous: Verifying X.509 Certificates](05-verifying-x509-certificates.md) | [Next: Setting up a Simulated Device (C#) →](07a-setting-up-csharp-device.md)
