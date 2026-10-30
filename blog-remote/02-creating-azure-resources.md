## **Creating DPS, IoT Hub and ADR Instances**

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

Welcome back to this series of posts exploring the latest features of Azure IoT Hub, Device Provisioning Service (DPS), and Azure Device Registry (ADR).

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

Now that you know what DPS does, let's stand up the Azure resources. We'll
create a resource group, a user-assigned managed identity, an ADR namespace, a
new IoT Hub, and DPS. The walkthrough below retains our Azure CLI approach,
updated to match the current Microsoft Learn
[Azure CLI deployment method](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).
We'll run the commands in PowerShell 7, using backticks for line continuation.
The Learn guide is the reference for the commands, not a replacement for the
steps here.

---

### Step-by-Step Azure CLI Tutorial

Follow along post-by-post to understand **why** and **how** everything works. You'll learn:

-   DPS concepts and architecture
-   Azure resource setup
-   X.509 certificate workflows
-   .NET implementation details

### Prerequisites

-   Azure subscription
-   Azure CLI 2.73.0 or later, Python 3.10 or later, and `azure-iot`
    extension version 1.0.0b2 or later, installed with preview support
    (`az extension add --name azure-iot --allow-preview`)
-   PowerShell 7+
-   Rights to create resources and assign roles at the required scopes, such
    as Owner, or Contributor together with User Access Administrator
-   A supported region: Australia East, Japan East, Central US, or UK South

### What We're Building

1.  Resource group and user-assigned managed identity
2.  ADR namespace with certificate management and a policy
3.  New IoT Hub linked to the namespace
4.  DPS linked to both the namespace and IoT Hub
5.  Role assignments, policy synchronization, and enrollment configuration

> **Preview warning:** These features are in public preview and aren't
> recommended for production. Existing IoT Hubs can't be upgraded to ADR
> integration; create a new hub. DPS is required for every IoT Hub preview
> scenario. Starting October 2026, ADR integration and certificate management
> are free during preview, while IoT Hub and DPS are billed at standard rates.

---

### Prepare the CLI Environment

First, sign in and select the subscription where you want to create the
preview resources:

```powershell
az login
az account list --output table
az account set --subscription "<your-subscription-name-or-id>"
az account show --output table
```

Check the installed extensions:

```powershell
az extension list --output table
```

If `azure-iot` is already installed, remove it before installing the
preview-enabled version, as directed by the current Learn guide:

```powershell
az extension remove --name azure-iot
```

If it isn't installed, skip the removal. Install it with preview support and
check that the installed version is **1.0.0b2 or later**:

```powershell
az extension add --name azure-iot --allow-preview
az extension list --output table
```

### Step 1: Set Variables and Create the Resource Group

Declare names once so the commands use the same resources throughout. Choose
unique names before running the creation commands; this is a new-environment
walkthrough, not an in-place upgrade of an existing hub.

```powershell
$subscriptionId = az account show --query id -o tsv
$location = "uksouth" # Or australiaeast, japaneast, or centralus
$unique = "dev001"             # make it unique

$resourceGroup = "$unique-skittlesorter-rg"
$iotHubName = "$unique-skittlesorter-hub"   # must be globally unique
$dpsName = "$unique-skittlesorter-dps"      # must be globally unique
$adrNamespace = "skittlesorter-preview" # choose a unique lowercase, letters-and-hyphens name
$credentialPolicyName = "cert-policy"
$userIdentity = "$unique-skittlesorter-uami"
$enrollmentGroupName = "$unique-skittlesorter-group"
$registrationId = "$unique-skittlesorter"
```

The namespace name must contain only lowercase letters and internal hyphens,
not digits or leading/trailing hyphens. Keep personal or sensitive information
out of the publicly discoverable IoT Hub name.

Now we create the container for everything.

```powershell
az group create --name $resourceGroup --location $location
```

### Step 2: Configure App Privileges

Now we configure the IoT Hub service principal permissions at the resource
group scope, as required by the current deployment guide.

```powershell
az role assignment create `
        --assignee "89d10474-74af-4874-99a7-c23c2f643083" `
        --role "Contributor" `
        --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup" `
        --output none
```

### Step 3: Create a User-Assigned Managed Identity

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
  --enable-certificate-management true `
  --policy-name $credentialPolicyName
```

Certificate management creates a namespace root CA and an issuing-CA policy.
The default certificate validity is 30 days; custom policies can set a
1–30-day validity period.

Namespace creation also configures a system-assigned managed identity and
can take up to five minutes. Verify the identity, root CA credential, and
named policy before continuing:

```powershell
az iot adr ns show `
        --name $adrNamespace `
        --resource-group $resourceGroup

az iot adr ns credential show `
        --namespace $adrNamespace `
        --resource-group $resourceGroup

az iot adr ns policy show `
        --namespace $adrNamespace `
        --resource-group $resourceGroup `
        --name $credentialPolicyName
```

Retrieve the namespace resource ID and assign the managed identity the Azure
Device Registry Contributor and Onboarding roles:

```powershell
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

### Step 5: Create IoT Hub

Create a **new** IoT Hub. In `azure-iot` 1.0.0b2, ADR namespace links are
created separately after both the Hub and DPS exist. The preview uses the
`GEN2` SKU; existing hubs have no upgrade path to ADR integration.

```powershell
az iot hub create `
        --name $iotHubName `
        --resource-group $resourceGroup `
        --location $location `
        --sku GEN2 `
        --mi-user-assigned $uamiResourceId `
        --output none

$hubResourceId = az iot hub show `
        --name $iotHubName `
        --resource-group $resourceGroup `
        --query id -o tsv
```

Verify that the hub has the managed identity and namespace properties
configured:

```powershell
az iot hub show `
        --name $iotHubName `
        --resource-group $resourceGroup `
        --output json
```

### Step 6: Create DPS

DPS is the front door. It will handle attestation and hand out hubs.

```powershell
az iot dps create `
        --name $dpsName `
        --resource-group $resourceGroup `
        --location $location `
        --mi-user-assigned $uamiResourceId `
        --output none

$dpsResourceId = az iot dps show `
        --name $dpsName `
        --resource-group $resourceGroup `
        --query id -o tsv
```

**Get DPS ID Scope:**

First verify its managed identity and namespace properties, then capture the
ID Scope for the device application:

```powershell
az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --output json

$idScope = az iot dps show `
  --name $dpsName `
  --resource-group $resourceGroup `
  --query properties.idScope -o tsv

Write-Host "DPS ID Scope: $idScope" -ForegroundColor Green
```

### Step 7: Link DPS and IoT Hub to the ADR Namespace

The 1.0.0b2 extension moved namespace linking out of the Hub and DPS create
commands. Link DPS first, then the Hub. This command also validates and
configures the required service roles; the signed-in user needs permission to
create role assignments.

```powershell
az iot adr ns link add `
        --namespace $adrNamespace `
        --resource-group $resourceGroup `
        --dps-endpoint-name $dpsName `
        --dps-resource-id $dpsResourceId `
        --dps-user-assigned-mi $uamiResourceId `
        --hub-endpoint-name $iotHubName `
        --hub-resource-id $hubResourceId `
        --hub-user-assigned-mi $uamiResourceId `
        --output none
```

### Step 8: Link DPS to IoT Hub

The ADR namespace link above is separate from DPS's provisioning link to the
Hub. Create the DPS-to-Hub link, then verify it appears in DPS:

```powershell
az iot dps linked-hub create `
        --dps-name $dpsName `
        --resource-group $resourceGroup `
        --hub-name $iotHubName `
        --output none

az iot dps linked-hub list `
        --dps-name $dpsName `
        --resource-group $resourceGroup
```

### Step 9: Sync Credential Policy

Next up we sync ADR credentials into IoT Hub so certs verify correctly.

**Sync credential policy to IoT Hub:**

```powershell
az iot adr ns credential sync `
        --namespace $adrNamespace `
        --resource-group $resourceGroup `
        --output json
```

**List certificates in IoT Hub:**

```powershell
az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

Verify the hub has registered the CA certificate.

### Step 10: Create an Enrollment Group

Create an enrollment group and assign the namespace policy. This example uses
the default symmetric-key attestation; the onboarding credential is distinct
from the operational certificate issued by the policy.
Because our policy is named `cert-policy` rather than `default`, pass its name
explicitly:

```powershell
az iot dps enrollment-group create `
        --dps-name $dpsName `
        --resource-group $resourceGroup `
        --enrollment-id $enrollmentGroupName `
        --credential-policy $credentialPolicyName
```

This completes the current Learn CLI setup using symmetric-key onboarding.
The X.509 onboarding path used by the device application needs the bootstrap
CA and enrollment configuration in
[Part 4](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/).
Keep the operational-certificate policy attached when configuring that
enrollment; creating a namespace policy alone doesn't assign it to devices.

### Verify in Portal

Now we check:

-   Resource Group shows IoT Hub, DPS, ADR
-   DPS → Linked IoT hubs lists your hub
-   DPS → Overview shows the ID Scope
-   IoT Hub → Certificates includes the synced CA

### Troubleshooting

-   **Name already exists**: Pick a new `$unique`.
-   **Unsupported region**: Use one of the supported regions listed above for
    IoT Hub, ADR, and DPS.
-   **Role assignment fails**: Wait 1–2 minutes, then retry.
-   **No CA in IoT Hub**: Re-run the credential-policy sync.

### What We Finished

- Resource group created
- New IoT Hub created with ADR integration (`GEN2`)
- DPS created and ID Scope captured
- ADR namespace + credential policy ready
- Links and roles wired up
- Enrollment group linked to the ADR certificate policy

Next up we explain onboarding credentials, operational certificates, and the
CSR workflow. For the current complete setup walkthrough, see the
[Microsoft Learn deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).

---

[Next: Creating X.509 Certificates >](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/)
