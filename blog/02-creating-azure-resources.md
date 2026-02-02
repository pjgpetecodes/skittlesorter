# Creating DPS and IoT Hub Instances and Linking Them

[← Previous: A Primer on DPS](01-primer-on-dps.md) | [Next: Creating X.509 Certificates →](03-x509-and-csr-workflows.md)

---

## Time to Build!

Now that you know what DPS does, let's stand up the Azure resources. We'll create a resource group, IoT Hub, and DPS, then link them. We'll also prep ADR so certificate policies are ready for issuance.

### Quick Run: Automation Scripts

We provide two helper scripts depending on your setup needs:

#### Option A: Full ADR + X.509 Setup (Recommended)
This script automates everything: Azure resources, ADR integration, X.509 certificates, DPS enrollment, and certificate verification.

```powershell
# Navigate to scripts directory
cd scripts

# Run the full setup (creates RG, UAMI, ADR Namespace, IoT Hub, DPS, and enrollment)
.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "eastus" `
  -IoTHubName "my-iothub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adrnamespace-001" `
  -UserIdentity "my-uami" `
  -RegistrationId "my-device" `
  -EnrollmentGroupId "my-device-group" `
  -AttestationType "X509"
```

The script will output:
- DPS ID Scope (needed for device config)
- Certificate paths (for device authentication)
- Azure resource details
- Enrollment group information

#### Option B: X.509 Only Setup (No ADR)
Use this for traditional X.509 workflows without Microsoft certificate management. Requires existing DPS/IoT Hub.

```powershell
# Navigate to scripts directory
cd scripts

# Run X.509 certificate setup (generates certs and creates enrollment)
.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg" `
  -EnrollmentGroupId "my-device-group"
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

```powershell
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location

$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope -o tsv

Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

## Step 4: Create ADR Namespace + Credential Policy

ADR backs certificate issuance. We create the namespace and a policy that uses Microsoft-managed CA.

```powershell
az iot ops asset endpoint create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location `
  --certificate-authority-name microsoft-managed `
  --identity-type SystemAssigned

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

```powershell
az identity create `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --location $location

$uamiPrincipalId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query principalId -o tsv

$adrResourceId = az iot ops asset endpoint show `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --query id -o tsv

az role assignment create `
  --assignee $uamiPrincipalId `
  --role "Device Registry Contributor" `
  --scope $adrResourceId

$hubIdentityPrincipalId = az iot hub show `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --query identity.principalId -o tsv

az role assignment create `
  --assignee $hubIdentityPrincipalId `
  --role "Device Registry Contributor" `
  --scope $adrResourceId
```

## Step 6: Link DPS to IoT Hub and ADR

Onwards we wire everything together.

```powershell
$hubConnectionString = az iot hub connection-string show `
  --hub-name $iotHubName `
  --resource-group $resourceGroup `
  --policy-name iothubowner `
  --query connectionString -o tsv

az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --connection-string $hubConnectionString `
  --location $location

$uamiResourceId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query id -o tsv

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

```powershell
az iot hub device-identity credential-policy sync `
  --hub-name $iotHubName `
  --resource-group $resourceGroup

az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

You should see the Microsoft-managed CA in the list.

## Step 8: Create Enrollment Group (X.509 + CSR)

We'll use X.509 certificates for attestation and CSR for certificate issuance.

```powershell
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type x509 `
  --ca-name "$registrationId-intermediate" `
  --iot-hub-host-name "$iotHubName.azure-devices.net" `
  --provisioning-status enabled `
  --credential-policy $credentialPolicyName

$enrollmentGroup = az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  -o json | ConvertFrom-Json -AsHashTable

Write-Host "Enrollment Group created with X.509 attestation" -ForegroundColor Cyan
```

## Step 9: Capture What You Need for the Device

Collect these values for the next post:

- IdScope: `$idScope`
- RegistrationId: `$registrationId`
- EnrollmentGroupKeyBase64: `$enrollmentKey`
- ProvisioningHost: `global.azure-devices-provisioning.net`
- CredentialPolicy: `$credentialPolicyName`

Sample config:

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "dev001-skittlesorter",
    "AttestationMethod": "SymmetricKey",
    "EnrollmentGroupKeyBase64": "your-enrollment-key-here",
    "ProvisioningHost": "global.azure-devices-provisioning.net"
  }
}
```

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
✅ Enrollment group with symmetric key + CSR path

Next up we generate the X.509 material and talk CSR workflows.

---

[Next: Creating X.509 Certificates →](03-x509-and-csr-workflows.md)
