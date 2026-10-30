# Creating DPS and IoT Hub Instances and Linking Them

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

[← Previous: A Primer on DPS](01-primer-on-dps.md) | [Next: Creating X.509 Certificates →](03-x509-and-csr-workflows.md)

---

## Time to Build!

Now that you know what DPS does, let's stand up the Azure resources. We'll create a resource group, IoT Hub, and DPS, then link them. We'll also prep ADR so certificate policies are ready for issuance.

### Quick Run: Automation Scripts

We provide two helper scripts depending on your setup needs:

#### Option A: Full ADR + X.509 Setup (Recommended)
This script automates everything: Azure resources, ADR integration, and X.509 certificates.

```powershell
# Navigate to scripts directory
cd scripts

# Run the full setup (creates RG, UAMI, ADR Namespace, IoT Hub, DPS)
.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "uksouth" `
  -IoTHubName "my-iothub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adrnamespace-uniquesuffix" `
  -UserIdentity "my-uami" `
  -RegistrationId "my-device"
```

The script will output:
- DPS ID Scope (needed for device config)
- Azure resource details
- ADR namespace and credential policy information

#### Option B: X.509 Certificate Setup Only
If you already have Azure resources but need X.509 certificates:

```powershell
# Navigate to scripts directory
cd scripts

# Run X.509 certificate setup (generates certs, verifies with DPS)
.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg"
```

Output includes:
- Root CA, Intermediate CA, and Device certificate paths
- Certificate thumbprints
- Verification status in DPS

### Update Device Configuration

Once the script completes, update your device settings in `appsettings.json`:

```json
{
  "IoTHub": {
    "DpsProvisioning": {
      "IdScope": "0ne001XXXXX",  // From script output
      "RegistrationId": "my-device",
      "AttestationMethod": "X509",
      "AttestationCertPath": "C:\\path\\to\\scripts\\certs\\device\\device.pem",
      "AttestationKeyPath": "C:\\path\\to\\scripts\\certs\\device\\device.key",
      "AttestationCertChainPath": "C:\\path\\to\\scripts\\certs\\ca\\chain.pem"
    }
  },
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ResourceGroupName": "my-iot-rg",
    "NamespaceName": "my-adrnamespace-uniquesuffix"
  }
}
```

### Run the Device Application

```powershell
dotnet run --project ../src/skittlesorter.csproj
```

---

### Prerequisites

- Azure subscription
- Azure CLI 2.73.0+, Python 3.10+, and `azure-iot` extension 1.0.0b2 or later, installed with preview support
- PowerShell 7+
- Rights to create resource groups and role assignments

Sign in, select your subscription, and install the preview-enabled Azure IoT
extension. If `azure-iot` is already installed, remove it before reinstalling
as shown in the current Microsoft Learn guide:

```powershell
az login
az account list --output table
az account set --subscription "<your-subscription-name-or-id>"
az extension list --output table
az extension remove --name azure-iot # Skip if it isn't installed
az extension add --name azure-iot --allow-preview
az extension list --output table # Confirm version 1.0.0b2 or later
```

The IoT Hub service principal needs Contributor access at the resource group
scope. The signed-in identity also needs permission to create role assignments.

### What We're Building

1. Resource Group (keeps everything together)  
2. IoT Hub (where devices send telemetry)  
3. DPS (handles zero-touch provisioning)  
4. ADR namespace + credential policy (issues certs)  
5. Links + roles so the pieces talk to each other

---

## Step 1: Set Variables

We declare names once so you can rerun easily.

```powershell
$subscriptionId = az account show --query id -o tsv
$location = "uksouth" # Or australiaeast, japaneast, or centralus
$unique = "dev001"             # make it unique

$resourceGroup = "$unique-skittlesorter-rg"
$iotHubName = "$unique-skittlesorter-hub"   # must be globally unique
$dpsName = "$unique-skittlesorter-dps"      # must be globally unique
$adrNamespace = "skittlesorter-preview-uniquesuffix" # choose a unique lowercase name with letters and hyphens only
$credentialPolicyName = "cert-policy"
$userIdentity = "$unique-skittlesorter-uami"
$enrollmentGroupName = "$unique-skittlesorter-group"
$registrationId = "$unique-skittlesorter"
```

Now we create the container for everything.

```powershell
az group create --name $resourceGroup --location $location
```

Configure the IoT Hub service principal's required resource-group role:

```powershell
az role assignment create `
  --assignee "89d10474-74af-4874-99a7-c23c2f643083" `
  --role "Contributor" `
  --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup"
```

## Step 2: Create the Managed Identity

The current deployment method creates a new IoT Hub with ADR integration.
Existing IoT Hubs can't be upgraded. Create a user-assigned managed identity
for IoT Hub and DPS.

```powershell
az identity create `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --location $location

$uamiResourceId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query id -o tsv

$uamiPrincipalId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query principalId -o tsv
```

## Step 3: Create ADR Namespace and Assign Its Roles

Enable certificate management to create the namespace root CA and named
certificate policy. Certificate management is optional for IoT Hub preview
deployments; DPS is required in all cases.

```powershell
az iot adr ns create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location `
  --enable-certificate-management true `
  --policy-name $credentialPolicyName

$namespaceResourceId = az iot adr ns show `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --query id -o tsv

az role assignment create `
  --assignee $uamiPrincipalId `
  --role "a5c3590a-3a1a-4cd4-9648-ea0a32b15137" `
  --scope $namespaceResourceId

az role assignment create `
  --assignee $uamiPrincipalId `
  --role "547f7f0a-69c0-4807-bd9e-0321dfb66a84" `
  --scope $namespaceResourceId
```

## Step 4: Create the IoT Hub

Use the GEN2 SKU and managed identity. In `azure-iot` 1.0.0b2, ADR namespace
links are created separately after both the Hub and DPS exist.

```powershell
az iot hub create `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --location $location `
  --sku GEN2 `
  --mi-user-assigned $uamiResourceId

$iotHubResourceId = az iot hub show `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --query id -o tsv

```

## Step 5: Create DPS, Link the Hub, and Synchronize Credentials

DPS is required for every IoT Hub preview scenario. Create it with the managed
identity; the 1.0.0b2 extension no longer accepts ADR namespace inputs on
resource creation.

```powershell
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location `
  --mi-user-assigned $uamiResourceId

$dpsResourceId = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query id -o tsv

# Link DPS first, then the Hub; this command validates/configures required service roles.
az iot adr ns link add `
  --namespace $adrNamespace `
  --resource-group $resourceGroup `
  --dps-endpoint-name $dpsName `
  --dps-resource-id $dpsResourceId `
  --dps-user-assigned-mi $uamiResourceId `
  --hub-endpoint-name $iotHubName `
  --hub-resource-id $iotHubResourceId `
  --hub-user-assigned-mi $uamiResourceId

az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --hub-name $iotHubName
```

Synchronize the ADR credentials and inspect the hub certificate list:

```powershell
az iot adr ns credential sync `
  --namespace $adrNamespace `
  --resource-group $resourceGroup

az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup

$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope -o tsv
Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

Create an enrollment group and explicitly attach the named policy when using
certificate management. Configure the group's onboarding attestation method
separately; the ADR policy is for operational certificate issuance.

```powershell
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --credential-policy $credentialPolicyName
```

## What You'll Need for Device Configuration

Collect these values for later posts:

- **IdScope**: `$idScope` (from DPS overview)
- **RegistrationId**: `$registrationId` (your device name)
- **ProvisioningHost**: `global.azure-devices-provisioning.net`
- **CredentialPolicy**: `$credentialPolicyName`

You'll use these when configuring your device application in Post 06.

## Verify in Portal

Now we check:
- Resource Group shows IoT Hub, DPS, ADR
- DPS → Linked IoT hubs lists your hub
- DPS → Overview shows the ID Scope
- IoT Hub → Certificates includes the synced CA

## Troubleshooting

- **Name already exists**: Pick a new `$unique`.
- **Unsupported region**: Use Australia East, Japan East, Central US, or UK South.
- **Role assignment fails**: Wait 1–2 minutes, then retry.
- **No CA in IoT Hub**: Re-run the credential-policy sync.

## What We Finished

✅ Resource group created  
✅ IoT Hub created (Standard)  
✅ DPS created and ID Scope captured  
✅ ADR namespace + credential policy ready  
✅ Links and roles wired up

Next up we generate the X.509 bootstrap certificates and explore CSR workflows.

---

[Next: Understanding X.509 and CSR Workflows →](03-x509-and-csr-workflows.md)
