# Creating DPS and IoT Hub Instances and Linking Them

[← Previous: A Primer on DPS](01-primer-on-dps.md) | [Next: Creating X.509 Certificates →](03-creating-x509-certificates.md)

---

## Time to Build!

Now that you understand DPS conceptually, let's create the actual Azure resources. We'll set up a resource group, an IoT Hub, and a DPS instance, then link them together.

## Prerequisites

- Azure subscription
- Azure CLI installed and configured (`az login`)
- Azure CLI IoT extension (`az extension add --name azure-iot`)
- Basic familiarity with Azure portal

## Resource Overview

Let's clarify what we're building. These three resources work together to enable device provisioning:

We'll create three Azure resources:
1. **Resource Group** - Logical container for our resources
2. **IoT Hub** - Where devices connect and send telemetry
3. **Device Provisioning Service (DPS)** - Handles device registration

## Step 1: Set Variables

Before we create anything, let's define the resource names and location we'll use. This keeps the setup modular and easy to update.

```powershell
# Define your Azure resources
$resourceGroup = "iot-x509-demo-rg"
$location = "eastus"
$iotHubName = "my-iot-hub-x509"  # Must be globally unique
$dpsName = "my-dps-x509"         # Must be globally unique

# Verify you're logged in
az account show
```

## Step 2: Create Resource Group

Create the resource group to keep everything related together before we add IoT Hub and DPS. Now we need to establish this container so the following resources stay organized.

```powershell
az group create `
  --name $resourceGroup `
  --location $location
```

**Expected output:**
```json
{
  "id": "/subscriptions/.../resourceGroups/iot-x509-demo-rg",
  "location": "eastus",
  "name": "iot-x509-demo-rg",
  "properties": {
    "provisioningState": "Succeeded"
  }
}
```

## Step 3: Create IoT Hub

Now we need to create the IoT Hub. Devices will ultimately be registered here after they connect through DPS. IoT Hub is where devices authenticate, send telemetry, and receive commands. We're using the S1 Standard tier because the free tier (F1) doesn't support DPS.

```powershell
az iot hub create `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --sku S1 `
  --partition-count 2
```

**Important notes:**
- IoT Hub must be **Standard tier (S1 or higher)** for DPS support
- Free tier (F1) does NOT support DPS
- This takes 2-3 minutes to complete

**Expected output:**
```json
{
  "id": "/subscriptions/.../providers/Microsoft.Devices/IotHubs/my-iot-hub-x509",
  "location": "eastus",
  "name": "my-iot-hub-x509",
  "properties": {
    "state": "Active",
    "provisioningState": "Succeeded"
  },
  "sku": {
    "name": "S1",
    "tier": "Standard",
    "capacity": 1
  }
}
```

## Step 4: Create DPS Instance

Onwards we provision the DPS instance that will handle device authentication, enrollment validation, and hub assignment. DPS is the entry point new devices use before landing in IoT Hub.

```powershell
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location
```

**This creates the DPS instance** (takes 1-2 minutes).

## Step 5: Link DPS to IoT Hub

Link DPS to IoT Hub so it can create device identities after validating them. Without this link, DPS can verify certificates but cannot register devices in IoT Hub. Onwards we enable DPS to write into IoT Hub.

```powershell
# Get the IoT Hub connection string
$iotHubConnectionString = az iot hub connection-string show `
  --hub-name $iotHubName `
  --query connectionString `
  -o tsv

# Link DPS to IoT Hub
az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --connection-string $iotHubConnectionString
```

**Expected output:**
```json
{
  "allocationWeight": 1,
  "applyAllocationPolicy": true,
  "connectionString": "HostName=my-iot-hub-x509.azure-devices.net;...",
  "location": "eastus",
  "name": "my-iot-hub-x509.azure-devices.net"
}
```

## Step 6: Get DPS ID Scope

Retrieve the ID Scope, the unique identifier devices use to locate and connect to your DPS instance. You'll embed this value in your device configuration.

```powershell
$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope `
  -o tsv

Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

**Save this value** - you'll need it when configuring devices.

Example: `0ne00123ABC`

## Verify in Azure Portal

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Go to your Resource Group
3. You should see:
   - ✅ IoT Hub (my-iot-hub-x509)
   - ✅ Device Provisioning Service (my-dps-x509)

4. Click on DPS → **Linked IoT hubs**
   - You should see your IoT Hub listed
   - Status should be "Active"

5. Click on DPS → **Overview**
   - Note the **ID Scope** (starts with "0ne")
   - Note the **Service endpoint** (global.azure-devices-provisioning.net)

## What We've Accomplished

✅ Created a resource group for organization  
✅ Created an IoT Hub (Standard tier) where devices will connect  
✅ Created a DPS instance for zero-touch provisioning  
✅ Linked DPS to IoT Hub (allows device registration)  
✅ Retrieved the DPS ID Scope (needed for device code)  

## Next Steps

Now that our Azure infrastructure is ready, we'll create the X.509 certificates that devices will use for authentication.

## Troubleshooting

**Error: "Name already exists"**
- IoT Hub and DPS names must be globally unique
- Try adding your initials or a number: `my-iot-hub-x509-pj`

**Error: "This operation is not supported for pricing tier 'F1'"**
- You're using the free tier IoT Hub
- Upgrade to S1: `--sku S1`

**Error: "Linked hub already exists"**
- The link is already created
- Verify with: `az iot dps linked-hub list --dps-name $dpsName --resource-group $resourceGroup`

---

[← Previous: A Primer on DPS](01-primer-on-dps.md) | [Next: Creating X.509 Certificates →](03-creating-x509-certificates.md)
