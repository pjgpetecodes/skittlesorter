# Creating DPS and IoT Hub Instances and Linking Them

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
  -Location "eastus" `
  -IoTHubName "my-iothub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adrnamespace-001" `
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
    "NamespaceName": "my-adrnamespace-001"
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
- Azure CLI + IoT extension (`az extension add --name azure-iot --allow-preview`)
- PowerShell 7+
- Rights to create resource groups and role assignments

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
$location = "eastus"
$unique = "dev001"             # make it unique

$resourceGroup = "$unique-skittlesorter-rg"
$iotHubName = "$unique-skittlesorter-hub"   # must be globally unique
$dpsName = "$unique-skittlesorter-dps"      # must be globally unique
$adrNamespace = "$unique-skittlesorter-adr" # lowercase only
$credentialPolicyName = "cert-policy"
$userIdentity = "$unique-skittlesorter-uami"
$enrollmentGroupName = "$unique-skittlesorter-group"
$registrationId = "$unique-skittlesorter"
```

Now we create the container for everything.

```powershell
az group create --name $resourceGroup --location $location
```

## Step 2: Create IoT Hub

IoT Hub is where devices will land after DPS assigns them. We use S1 (preview features need Standard).

```powershell
az iot hub create `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --location $location `
  --sku S1 `
  --partition-count 4
```

## Step 3: Create DPS

DPS is the front door. It will handle attestation and hand out hubs.

**Create DPS instance:**

```powershell
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location
```

**Get DPS ID Scope:**

```powershell
$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope -o tsv

Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

## Step 4: Create ADR Namespace + Credential Policy

ADR backs certificate issuance. We create the namespace and a policy that uses Microsoft-managed CA.

**Create ADR namespace:**

```powershell
az iot ops asset endpoint create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location `
  --certificate-authority-name microsoft-managed `
  --identity-type SystemAssigned
```

**Create credential policy:**

```powershell
az iot hub device-identity credential-policy create `
  --namespace $adrNamespace `
  --resource-group $resourceGroup `
  --policy-name $credentialPolicyName `
  --certificate-authority microsoft-managed `
  --validity-period P30D `
  --renewal-window P7D
```

## Step 5: Create Managed Identity and Assign Roles

We need permissions for DPS and IoT Hub to read ADR. Now we create UAMI and grant access.

**Create user-assigned managed identity:**

```powershell
az identity create `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --location $location
```

**Get identity principal ID and ADR resource ID:**

```powershell
$uamiPrincipalId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query principalId -o tsv

$adrResourceId = az iot ops asset endpoint show `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --query id -o tsv
```

**Assign Device Registry Contributor role to identity:**

```powershell
az role assignment create `
  --assignee $uamiPrincipalId `
  --role "Device Registry Contributor" `
  --scope $adrResourceId
```

**Get IoT Hub identity principal ID:**

```powershell
$hubIdentityPrincipalId = az iot hub show `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --query identity.principalId -o tsv
```

**Assign Device Registry Contributor role to IoT Hub:**

```powershell
az role assignment create `
  --assignee $hubIdentityPrincipalId `
  --role "Device Registry Contributor" `
  --scope $adrResourceId
```

## Step 6: Link DPS to IoT Hub and ADR

Onwards we wire everything together.

**Get IoT Hub connection string:**

```powershell
$hubConnectionString = az iot hub connection-string show `
  --hub-name $iotHubName `
  --resource-group $resourceGroup `
  --policy-name iothubowner `
  --query connectionString -o tsv
```

**Create linked hub in DPS:**

```powershell
az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --connection-string $hubConnectionString `
  --location $location
```

**Get identity resource ID:**

```powershell
$uamiResourceId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query id -o tsv
```

**Update DPS with ADR and identity configuration:**

```powershell
az iot dps update `
  --name $dpsName `
  --resource-group $resourceGroup `
  --set properties.deviceRegistry.namespace=$adrNamespace `
  --set properties.deviceRegistry.resourceId=$adrResourceId `
  --set identity.type=UserAssigned `
  --set identity.userAssignedIdentities.\"$uamiResourceId\"={}
```

## Step 7: Sync Credential Policy

Next up we sync ADR credentials into IoT Hub so certs verify correctly.

**Sync credential policy to IoT Hub:**

```powershell
az iot hub device-identity credential-policy sync `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

**List certificates in IoT Hub:**

```powershell
az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

You should see the Microsoft-managed CA in the list.

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
- **Free tier blocked**: Use `--sku S1` (F1 does not support DPS).
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
