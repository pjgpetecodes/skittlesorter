# Azure Setup

Provision Azure resources for IoT and device registration.

> **📖 Primary Setup Guide:** This project follows Microsoft's official setup workflow. For the authoritative and likely up to date, follow the step-by-step guide here;
> - **[Get started with ADR integration and Microsoft-backed X.509 certificate management in IoT Hub (Azure CLI)](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli)** ⭐ **Start here!**
>
> **Additional References:**
> - [Azure Device Provisioning Service (DPS)](https://learn.microsoft.com/azure/iot-dps/)
> - [Azure Device Registry](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
> - [DPS Enrollment Groups](https://learn.microsoft.com/azure/iot-dps/concepts-service#enrollment-group)
> - [X.509 Certificate Attestation](https://learn.microsoft.com/azure/iot-dps/concepts-x509-attestation)
>
> **Status:** This guide reflects the preview API (`2025-07-01-preview`) as of January 2026. Always check Microsoft's documentation for the latest updates.

## Table of Contents

0. [Variables](#variables) - Set once and reuse
1. [Prerequisites](#prerequisites) - Azure CLI and IoT extension setup
2. [Create Resource Group](#1-create-resource-group)
3. [Configure App Privileges](#2-configure-app-privileges)
4. [Create User-Assigned Managed Identity](#3-create-user-assigned-managed-identity)
5. [Create ADR Namespace with System-Assigned Identity and Default Policy](#4-create-adr-namespace-with-system-assigned-identity-and-default-policy)
6. [Assign UAMI role to access the ADR namespace](#5-assign-uami-role-to-access-the-adr-namespace)
7. [Add or Customize Credential Policy](#6-add-or-customize-credential-policy)
8. [Create IoT Hub (Preview) and Link ADR Namespace](#7-create-iot-hub-preview-and-link-adr-namespace)
9. [Assign IoT Hub roles to access the ADR namespace](#8-assign-iot-hub-roles-to-access-the-adr-namespace)
10. [Create DPS and Link IoT Hub and Namespace](#9-create-dps-and-link-iot-hub-and-namespace)
11. [Sync Credentials and Policies to IoT Hub](#10-sync-credentials-and-policies-to-iot-hub)
    - [Sync credentials and policies](#sync-credentials-and-policies)
    - [Validate IoT Hub CA certificate](#validate-iot-hub-ca-certificate)
12. [Create Enrollment Group](#11-create-enrollment-group)
    - [Option A: Symmetric Key Attestation](#option-a-symmetric-key-attestation-simpler) (Simpler)
    - [Option B: X.509 Certificate Attestation](#option-b-x509-certificate-attestation-more-secure) (More Secure)
13. [Review Configuration Values](#13-review-configuration-values)
14. [Configure Device Credentials](#14-configure-device-credentials)
## Variables

Set these variables once and reuse them throughout the commands below.

```powershell
# Subscription (GUID)
$subscriptionId = "<your-subscription-guid>"   # Get via: az account show --query id -o tsv

# Region
$location = "eastus"                           # Choose a supported region for IoT/ADR/DPS

# Unique suffix to ensure globally-unique names (customize)
$unique = "dev001"                              # e.g., $(Get-Date -Format 'yyyyMMddHHmmss') or a team/env tag

# Resource Group
$resourceGroup = "$unique-skittlesorter-rg"     # Unique per environment

# IoT Hub (preview)
$iotHubName = "$unique-skittlesorter-hub"       # Lowercase, globally unique (DNS label)

# Device Provisioning Service (DPS)
$dpsName = "$unique-skittlesorter-dps"          # Lowercase, unique within your subscription

# Azure Device Registry (ADR) namespace
$adrNamespace = "$unique-skittlesorter-adr"     # Lowercase; hyphens allowed; not at start/end

# ADR credential policy name
$credentialPolicyName = "cert-policy"          # Issuing CA policy name

# User-assigned managed identity
$userIdentity = "$unique-skittlesorter-uami"    # UAMI for cross-resource operations

# DPS enrollment group
$enrollmentGroupName = "$unique-skittlesorter-group"   # Enrollment group identifier

# Device registration id (used by DPS and scripts)
$registrationId = "$unique-skittlesorter"
```

Tips:
- Keep names consistent across resources (hub, dps, adr, group).
- Use the `$unique` prefix to avoid name collisions, especially for IoT Hub (globally unique).
- ADR namespace name must be lowercase; hyphens permitted but not at the beginning or end.
- IoT Hub names are typically lowercase and must be globally unique.
- Verify your subscription with `az account show --query id -o tsv`.

---

## Quick Reference

The steps below provide a quick reference aligned with the official Microsoft guide. For detailed explanations, prerequisites, and troubleshooting, **follow the [official Azure CLI setup guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli)**.

## Prerequisites

Install [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) and the IoT extension with preview features:

```powershell
# Sign in to Azure
az login
az account set --subscription "Your-Subscription-Name"

# Install/update Azure IoT extension with preview support
az extension remove --name azure-iot
az extension add --name azure-iot --allow-preview

# Verify version is at least 0.30.0b1
az extension list
```

> **Important:** The preview features require Azure IoT CLI extension version 0.30.0b1 or later.

## 1. Create Resource Group

```powershell
az group create --name $resourceGroup --location $location
```

## 2. Configure App Privileges

Grant the IoT Hub app principal Contributor on the resource group (fixed appId from the official guide).

```powershell
az role assignment create `
  --assignee "89d10474-74af-4874-99a7-c23c2f643083" `
  --role "Contributor" `
  --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup"
```

## 3. Create User-Assigned Managed Identity

```powershell
az identity create `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --location $location

$uamiResourceId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query id -o tsv
```

## 4. Create ADR Namespace with System-Assigned Identity and Default Policy

The certificate issuance feature requires Azure Device Registry (ADR) with a credential policy. Creating the namespace with a **system-assigned managed identity** can automatically generate a root CA credential and an issuing CA policy.

- Namespace name: lowercase letters and hyphens only; hyphens cannot start or end the name (e.g., `msft-namespace` is valid).
- `--enable-credential-policy` creates the default credential (root CA) and policy (issuing CA) for 30-day certificates; override with `--policy-name`.

```powershell
# Recommended: create ADR namespace with system-assigned identity + default credential policy
az iot adr ns create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location `
  --enable-credential-policy true `
  --policy-name $credentialPolicyName

# Optional: customize policy subject/validity
# az iot adr ns create ... --cert-subject "CN=skittlesorter" --cert-validity-days 45
```

> **Note:** Creating the ADR namespace with a system-assigned identity can take up to 5 minutes.

## 5. Assign UAMI role to access the ADR namespace

Grant the User-Assigned Managed Identity the **Azure Device Registry Contributor** role scoped to the ADR namespace. This allows the UAMI to manage IoT devices within the namespace.

```powershell
# Get UAMI principal ID
$uamiPrincipalId = az identity show `
  --name $userIdentity `
  --resource-group $resourceGroup `
  --query principalId -o tsv

# Get ADR namespace resource ID
$namespaceResourceId = az iot adr ns show `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --query id -o tsv

# Assign Azure Device Registry Contributor role to UAMI, scoped to the namespace
az role assignment create `
  --assignee $uamiPrincipalId `
  --role "a5c3590a-3a1a-4cd4-9648-ea0a32b15137" `
  --scope $namespaceResourceId
```

## 6. Add or Customize Credential Policy (Optional)

If you created the namespace without `--enable-credential-policy`, add a policy now. You can choose ECC or RSA and adjust validity.

```powershell
# Create namespace without credential policy
az iot adr ns create `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --location $location

# Add credential policy
az iot adr credential-policy create `
  --namespace-name $adrNamespace `
  --credential-policy-name $credentialPolicyName `
  --resource-group $resourceGroup `
  --certificate-type ECC `
  --validity-period-days 30
```

## 7. Create IoT Hub (Preview) and Link ADR Namespace

Create a new IoT Hub linked to the ADR namespace and with the user-assigned managed identity created earlier.

```powershell
az iot hub create `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --location $location `
  --sku GEN2 `
  --mi-user-assigned $uamiResourceId `
  --ns-resource-id $namespaceResourceId `
  --ns-identity-id $uamiResourceId
```

> **Important:** Because the IoT Hub will be publicly discoverable as a DNS endpoint, be sure to avoid entering any sensitive or personally identifiable information when you name it.

## 9. Assign IoT Hub roles to access the ADR namespace

Retrieve the ADR namespace managed identity principal ID and the IoT Hub resource ID, then grant permissions required for ADR to manage devices in the hub.

```powershell
# Get ADR namespace managed identity principal ID
$adrPrincipalId = az iot adr ns show `
  --name $adrNamespace `
  --resource-group $resourceGroup `
  --query identity.principalId -o tsv

# Get IoT Hub resource ID
$hubResourceId = az iot hub show `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --query id -o tsv

# Grant Contributor role to ADR identity on the IoT Hub
az role assignment create `
  --assignee $adrPrincipalId `
  --role "Contributor" `
  --scope $hubResourceId

# Grant IoT Hub Registry Contributor role to ADR identity
az role assignment create `
  --assignee $adrPrincipalId `
  --role "IoT Hub Registry Contributor" `
  --scope $hubResourceId
```

## 10. Create DPS and Link IoT Hub and Namespace

Create a new Device Provisioning Service instance linked to your ADR namespace. The DPS instance must be located in the same region as your ADR namespace.

```powershell
# Create DPS with ADR integration
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location `
  --mi-user-assigned $uamiResourceId `
  --ns-resource-id $namespaceResourceId `
  --ns-identity-id $uamiResourceId

# Link IoT Hub to DPS
az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --hub-name $iotHubName

# Verify IoT Hub is linked
az iot dps linked-hub list `
  --dps-name $dpsName `
  --resource-group $resourceGroup

# Verify DPS identity and ADR properties
az iot dps show --name $dpsName --resource-group $resourceGroup

# Get DPS ID Scope (save this for appsettings.json)
$dpsIdScope = az iot dps show --name $dpsName --resource-group $resourceGroup --query properties.idScope -o tsv
Write-Host "DPS ID Scope: $dpsIdScope"
```

## 11. Sync Credentials and Policies to IoT Hub

### Sync credentials and policies

Synchronize your ADR credential and policies to the IoT Hub so it registers the CA certificates and trusts leaf certificates issued by your configured policies.

```powershell
az iot adr ns credential sync `
  --namespace $adrNamespace `
  --resource-group $resourceGroup
```

### Validate IoT Hub CA certificate

Confirm your IoT Hub has registered its CA certificate:

```powershell
az iot hub certificate list `
  --hub-name $iotHubName `
  --resource-group $resourceGroup
```

Next: proceed to [Section 12 (Create Enrollment Group)](#11-create-enrollment-group) to set up enrollments and link your credential policy.

## 12. Create Enrollment Group

Choose one of the following attestation methods:

### Option A: Symmetric Key Attestation (Simpler)

```powershell
# Create enrollment group with symmetric key attestation
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type symmetrickey `
  --credential-policy-name $credentialPolicyName `
  --credential-policy-namespace $adrNamespace `
  --provisioning-status enabled

# Get the primary key (save this for appsettings.json)
$enrollmentKey = az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --query attestation.symmetricKey.primaryKey -o tsv
Write-Host "Enrollment Group Primary Key: $enrollmentKey"
```

**Skip to [Section 13 (Review Configuration Values)](#13-review-configuration-values)** if using symmetric key attestation.

### Option B: X.509 Certificate Attestation (More Secure)

This project also supports **X.509 intermediate attestation** with a proper certificate hierarchy:

```
Root CA (verified in DPS)
  └─ Intermediate CA (verified in DPS, used in enrollment)
      └─ Device Certificate (signed by intermediate)
```

**Why This Hierarchy?**

- **Root CA**: Trust anchor, verified via proof-of-possession (PoP), can be kept offline
- **Intermediate CA**: Operational signer, signs device certificates, can be rotated/revoked
- **Device Cert**: Device identity, authenticates to DPS, signed by intermediate

**⚠️ Important: Both Root AND Intermediate Must Be Verified**

DPS requires **proof-of-possession verification** for **BOTH** the root and intermediate certificates. If the intermediate is not verified, provisioning will fail with:

```
401 - CA certificate not found
```

We discovered this the hard way! Even if thumbprints match and the chain is correct, DPS will reject the device if the intermediate CA is not verified.

### Clean Up Old Resources (If Any)

Before running the setup script for the first time (or re-running after changes), clean up any existing certificates and enrollments:

```powershell
# Delete existing enrollment group (if any)
az iot dps enrollment-group delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id skittlesorter-group

# Delete old certificates from DPS (if any)
az iot dps certificate delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name skittlesorter-root `
  --etag "*"

az iot dps certificate delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name skittlesorter-intermediate `
  --etag "*"

# Delete local certificate files
Remove-Item -Recurse -Force certs/
```

### Automated Certificate Setup

Run the provided PowerShell script to automatically generate certificates and configure DPS:

```powershell
pwsh ./scripts/setup-x509-attestation.ps1 `
  -RegistrationId skittlesorter `
  -EnrollmentGroupId skittlesorter-group `
  -DpsName $dpsName `
  -ResourceGroup $resourceGroup `
  -CredentialPolicy $credentialPolicyName
```

**What This Script Does:**

1. **[1/8]** Creates certificate directory structure (`certs/root`, `certs/ca`, `certs/device`, `certs/issued`)
2. **[2/8]** Generates a complete certificate hierarchy:
   - **Root CA**: Self-signed, 4096-bit RSA, 10-year validity, `pathlen:1`
   - **Intermediate CA**: Signed by root, 4096-bit RSA, 5-year validity, `pathlen:0`
   - **Device Certificate**: Signed by intermediate, 2048-bit RSA, 1-year validity, `extendedKeyUsage=clientAuth`
3. **[3/8]** Extracts thumbprints for all certificates
4. **[4/8]** Uploads root CA to DPS and verifies ownership via proof-of-possession:
   - Generates verification code
   - Creates verification certificate with CN=\<code\>, signed by root
   - Verifies ownership (proves you control the root private key)
5. **[5/8]** Uploads intermediate CA to DPS and verifies ownership via proof-of-possession:
   - Generates verification code
   - Creates verification certificate with CN=\<code\>, signed by intermediate
   - Verifies ownership (proves you control the intermediate private key)
6. **[6/8]** Creates enrollment group with CA reference:
   - Uses `--ca-name` (references uploaded intermediate by name)
   - Links to credential policy for CSR-based certificate issuance
   - Sets intermediate as primary CA (it's the direct signer of device certificates)
7. **[7/8]** Updates `appsettings.json` with certificate paths
8. **[8/8]** Displays summary and verification status

**Key Files Generated:**

- `certs/root/root.pem`: Root CA certificate (upload & verify in DPS)
- `certs/root/root.key`: Root CA private key
- `certs/ca/ca.pem`: Intermediate CA certificate (upload & verify in DPS)
- `certs/ca/ca.key`: Intermediate CA private key
- `certs/ca/chain.pem`: Full chain (intermediate + root) for TLS presentation
- `certs/device/device.pem`: Device certificate (signed by intermediate)
- `certs/device/device.key`: Device private key
- `certs/device/device-full-chain.pem`: Full chain (device + intermediate + root)

**Verification Status Check:**

After the script completes, it will show:
```
Root CA isVerified: true
Intermediate CA isVerified: true
```

Both **must** be `true` for provisioning to work. If either is `false`, provisioning will fail with `401 - CA certificate not found`.

## 13. Review Configuration Values

Before updating `appsettings.json`, review all the values you'll need:

```powershell
# Display all configuration values
Write-Host ""
Write-Host "=========================================="
Write-Host "AZURE SETUP COMPLETE - Configuration Summary"
Write-Host "=========================================="
Write-Host ""
Write-Host "Resource Names:"
Write-Host "  Resource Group:        $resourceGroup"
Write-Host "  IoT Hub:               $iotHubName"
Write-Host "  DPS:                   $dpsName"
Write-Host "  ADR Namespace:         $adrNamespace"
Write-Host "  Enrollment Group:      $enrollmentGroupName"
Write-Host ""
Write-Host "Required for appsettings.json:"
Write-Host "  IdScope:               $dpsIdScope"
Write-Host "  RegistrationId:        $registrationId"
Write-Host ""
Write-Host "For Symmetric Key Attestation:"
Write-Host "  EnrollmentGroupKeyBase64:  $enrollmentKey"
Write-Host ""
Write-Host "For X.509 Certificate Attestation:"
Write-Host "  AttestationCertPath:        certs/device/device.pem"
Write-Host "  AttestationCertChainPath:   certs/device/device-full-chain.pem"
Write-Host ""
Write-Host "=========================================="
Write-Host "Copy these values to your appsettings.json"
Write-Host "=========================================="
Write-Host ""
```

## 14. Configure Device Credentials

**⚠️ SECURITY**: Never commit `appsettings.json` to source control as it contains secrets!

1. **Copy the template**:
   ```bash
   cp src/configuration/appsettings.template.json appsettings.json
   ```

2. **Update `appsettings.json`** based on your chosen attestation method:

**For Symmetric Key Attestation:**
   - `IdScope`: From DPS Overview
   - `AttestationMethod`: Set to `"SymmetricKey"` (default)
   - `EnrollmentGroupKeyBase64`: The enrollment group primary key (from step 3)
   - `RegistrationId`: Your device identifier (e.g., `skittlesorter`)

**For X.509 Certificate Attestation:**
   - `IdScope`: From DPS Overview
   - `AttestationMethod`: Set to `"X509"`
   - `AttestationCertPath`: Path to device certificate (auto-set by setup script)
**For complete setup instructions, always refer to the [official Microsoft guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).**
   - `AttestationCertChainPath`: Path to chain file (auto-set by setup script)
   - `RegistrationId`: Your device identifier (e.g., `skittlesorter`)

See [Configuration](./Configuration.md) for complete settings.

**Note**: For X.509, the setup script automatically updates `appsettings.json` with the correct certificate paths.

## Summary: Variables for appsettings.json

After completing all sections above, you'll have the following values needed for `appsettings.json`:

```powershell
# Display all values needed for configuration
Write-Host "================================================"
Write-Host "Copy these values to appsettings.json:"
Write-Host "================================================"
Write-Host "IdScope: $dpsIdScope"
Write-Host "RegistrationId: $registrationId"
Write-Host "EnrollmentGroupName: $enrollmentGroupName"
Write-Host ""
Write-Host "For Symmetric Key Attestation:"
Write-Host "  EnrollmentGroupKeyBase64: (See Section 13 output above)"
Write-Host ""
Write-Host "For X.509 Certificate Attestation:"
Write-Host "  AttestationCertPath: certs/device/device.pem"
Write-Host "  AttestationCertChainPath: certs/device/device-full-chain.pem"
Write-Host "================================================"
```

See [Configuration](./Configuration.md) for complete settings and detailed schema.

**Note**: For X.509, the `setup-x509-attestation.ps1` script automatically updates `appsettings.json` with the correct certificate paths.

## Related

For the latest Azure CLI commands and service updates, always refer to the official Microsoft documentation linked above.

- [DPS Provisioning](./DPS-Provisioning.md)
- [ADR Integration](./ADR-Integration.md)
- [Configuration](./Configuration.md)