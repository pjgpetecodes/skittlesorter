## **Creating DPS, IoT Hub and ADR Instances**

Welcome back to this series of posts exploring the latest features of Azure IoT Hub, Device Provisioning Service (DPS), and Azure Device Registration (ADR).

In the previous post we dove deeper into DPS and what's new.

```wordpress-block
<!-- wp:heading {"textColor":"#f6f6f4"} -->
<h2 class="wp-block-heading has-f-6-f-6-f-4-color has-text-color">Contents:</h2>
<!-- /wp:heading -->
```

```wordpress-block
<!-- wp:list {"ordered":true,"textColor":"#f6f6f4"} -->
<ol class="wp-block-list has-f-6-f-6-f-4-color has-text-color"><!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1" target="_blank" rel="noreferrer noopener">Introduction </a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2" target="_blank" rel="noreferrer noopener">A DPS Primer</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3" target="_blank" rel="noreferrer noopener">Creating DPS, IoT Hub and ADR Instances</a> (This Post)</li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4" target="_blank" rel="noreferrer noopener">Understanding X.509 and CSR Workflows</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5" target="_blank" rel="noreferrer noopener">The Device Application</a></li>
<!-- /wp:list-item --></ol>
<!-- /wp:list -->
```

### Time to Build!

Now that you know what DPS does, let's stand up the Azure resources. We'll create a resource group, IoT Hub, and DPS, then link them. We'll also prep ADR so certificate policies are ready for issuance.

### Two Ways to Use This Series

#### Option 1: Quick Start (Clone and Run)

```powershell
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter/scripts

# Full ADR + X.509 setup (recommended)
.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "eastus" `
  -IoTHubName "my-hub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adr-001" `
  -UserIdentity "my-uami"
```

Rename the fields above to suit your solution, then when you run it, the script will output the following:

-   DPS ID Scope (needed for device config)
-   Certificate paths (for device authentication)
-   Azure resource details
-   Enrollment group information

Or, if you have the Azure resources provisioned already, you can also just run the X.509 Setup script;

```json
.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg"
```

### Update Device Configuration

Once the script completes, update your device settings:

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

Once you've run one of the above two commands, you'll need to update your appsettings.json file with the results printed out at the end of the script execution.

You can then run the skittle sorter with;

```powershell
cd ..      # You should be in the skittlesorter directory
dotnet run --project src/skittlesorter.csproj
```

---

### Option 2: Step-by-Step Tutorial

Follow along post-by-post to understand **why** and **how** everything works. You'll learn:

-   DPS concepts and architecture
-   Azure resource setup
-   X.509 certificate workflows
-   .NET implementation details

### Prerequisites

-   Azure subscription
-   Azure CLI + IoT extension (`az extension add --name azure-iot --allow-preview`)
-   PowerShell 7+
-   Rights to create resource groups and role assignments

### What We're Building

1.  Resource Group (keeps everything together)
2.  IoT Hub (where devices send telemetry)
3.  DPS (handles zero-touch provisioning)
4.  ADR namespace + credential policy (issues certs)
5.  Links + roles so the pieces talk to each other

---

### Step 1: Set Variables and create the Resource Group

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

### Step 2: Configure App Privileges

Now we configure App Privileges (IoT Hub app principal)

```powershell
az role assignment create `
        --assignee "89d10474-74af-4874-99a7-c23c2f643083" `
        --role "Contributor" `
        --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup " `
        --output none 2>$null
```

### Step 3: Create Managed Identity and Assign Roles

We need permissions for DPS and IoT Hub to read ADR. Now we create UAMI and grant access.

First we create a reusable Azure managed identity so IoT Hub and DPS can securely access ADR without storing secrets.

```powershell
az identity create `
        --name $userIdentity `
        --resource-group $resourceGroup `
        --location $location `
        --output none
```

Now we retrieve the managed identity’s resource ID and save it to `$uamiResourceId` so we can attach that identity to IoT Hub and DPS in the next steps.

```powershell
$uamiResourceId = az identity show `
        --name $userIdentity `
        --resource-group $resourceGroup `
        --query id -o tsv
```

Now we retrieve the managed identity’s principal ID and store it in `$uamiPrincipalId` so we can grant it RBAC permissions on ADR.

```powershell
$uamiPrincipalId = az identity show `
        --name $userIdentity `
        --resource-group $resourceGroup `
        --query principalId -o tsv
```

### Step 4: Create ADR Namespace + Credential Policy

ADR backs certificate issuance. We create the namespace and a policy that uses Microsoft-managed CA.

**Create ADR namespace:**

```powershell
az iot adr ns create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location `
  --certificate-authority-name microsoft-managed `
  --identity-type SystemAssigned
```

Now we retrieve the Device Registry Namespace Resource ID.

```powershell
$namespaceResourceId = az iot adr ns show `
        --name $adrNamespace `
        --resource-group $resourceGroup `
        --query id -o tsv
```

Now we grant the managed identity the ADR contributor role at the namespace scope so it has permission to access and operate on that ADR namespace.

```powershell
# Assign UAMI role to access the ADR namespace
az role assignment create `
        --assignee $uamiPrincipalId `
        --role "a5c3590a-3a1a-4cd4-9648-ea0a32b15137" `
        --scope $namespaceResourceId `
        --output none
```

### Step 5: Create IoT Hub

Now it's time to create the IoT Hub where our devices will land after DPS assigns them. We need to use S1 as the preview features need Standard.

```powershell
az iot hub create `
        --name $ioTHubName `
        --resource-group $resourceGroup `
        --location $location `
        --sku s1 `
        --mi-user-assigned $uamiResourceId `
        --ns-resource-id $namespaceResourceId `
        --ns-identity-id $uamiResourceId `
        --output none
```

### Step 6: Create Managed Identity and Assign Roles

We need permissions for DPS and IoT Hub to read ADR. Now we create UAMI and grant access.

Now we fetch the ADR namespace’s managed identity principal ID and store it in `$adrPrincipalId` so we can grant that identity access to IoT Hub.

```powershell
$adrPrincipalId = az iot adr ns show `
        --name $adrNamespace `
        --resource-group $resourceGroup `
        --query identity.principalId -o tsv
```

Now we grant the ADR namespace identity Contributor access on the IoT Hub so ADR can manage required hub-side resources during provisioning.

```powershell
az role assignment create `
        --assignee $adrPrincipalId `
        --role "Contributor" `
        --scope $hubResourceId `
        --output none
```

Now we grant the ADR namespace identity the IoT Hub Registry Contributor role so it can create and manage device identities during provisioning.

```powershell
az role assignment create `
        --assignee $adrPrincipalId `
        --role "IoT Hub Registry Contributor" `
        --scope $hubResourceId `
        --output none
```

### Step 7: Create DPS

DPS is the front door. It will handle attestation and hand out hubs.

```powershell
az iot dps create `
        --name $dPSName `
        --resource-group $resourceGroup `
        --location $location `
        --mi-user-assigned $uamiResourceId `
        --ns-resource-id $namespaceResourceId `
        --ns-identity-id $uamiResourceId `
        --output none
```

**Get DPS ID Scope:**

```powershell
$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope -o tsv

Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

### Step 8: Link DPS to IoT Hub and ADR

Onwards we wire everything together.

```powershell
az iot dps linked-hub create `
        --dps-name $dpsName `
        --resource-group $resourceGroup `
        --hub-name $ioTHubName `
        --output none
```

### Step 9: Sync Credential Policy

Next up we sync ADR credentials into IoT Hub so certs verify correctly.

**Sync credential policy to IoT Hub:**

```powershell
az iot adr ns credential sync `
        --namespace $adrNamespace `
        --resource-group $resourceGroup `
        --output json 2>&amp;1
```

**List certificates in IoT Hub:**

```powershell
az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

You should see the Microsoft-managed CA in the list.

### Verify in Portal

Now we check:

-   Resource Group shows IoT Hub, DPS, ADR
-   DPS → Linked IoT hubs lists your hub
-   DPS → Overview shows the ID Scope
-   IoT Hub → Certificates includes the synced CA

### Troubleshooting

-   **Name already exists**: Pick a new `$unique`.
-   **Free tier blocked**: Use `--sku S1` (F1 does not support DPS).
-   **Role assignment fails**: Wait 1–2 minutes, then retry.
-   **No CA in IoT Hub**: Re-run the credential-policy sync.

### What We Finished

✅ Resource group created  
✅ IoT Hub created (Standard)  
✅ DPS created and ID Scope captured  
✅ ADR namespace + credential policy ready  
✅ Links and roles wired up  
✅ Enrollment group with symmetric key + CSR path

Next up we generate the X.509 material and talk CSR workflows.

---

[Next: Creating X.509 Certificates >](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/)
