# Azure IoT Hub + Device Registry preview setup

`setup.ps1` provisions a new IoT Hub, DPS, and Azure Device Registry
certificate-management environment using the refreshed public-preview CLI
workflow. It also writes a local sorter configuration using the DPS ID scope
and a device-specific symmetric key. It does not create device records or
provision a device until the sorter itself is run.

## Prerequisites

- Azure CLI 2.73.0 or later and PowerShell 7 or later.
- Python 3.10 or later for the Azure IoT CLI extension.
- An Azure subscription and permission to create resources and role assignments
  at the required scopes (for example, Owner, or Contributor plus User Access
  Administrator).
- A supported preview region: `australiaeast`, `japaneast`, `centralus`, or
  `uksouth`.

The script installs/upgrades the preview-enabled `azure-iot` extension and
registers the `Microsoft.DeviceRegistry` and `Microsoft.Devices` providers.
Microsoft's current [deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli)
is the source for the CLI workflow.

## Run

From the repository root, sign in and preview the requested deployment first:

```powershell
az login
az account set --subscription "<subscription-id>"

.\scripts\iot-hub-device-registry\setup.ps1 `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "my-iot-preview-rg" `
  -Location "uksouth" `
  -NamespaceName "my-iot-adr-namespace-002" `
  -HubName "my-iot-hub-002" `
  -DpsName "my-iot-dps-002" `
  -EnrollmentId "my-enrollment-group-002" `
  -WhatIf
```

After confirming the subscription, names, and region, run the same command
without `-WhatIf`. Resource names must be available in Azure; use lowercase
letters, numbers, and hyphens for the ADR namespace name. IoT Hub and DPS
names must also be globally unique.

The script is resumable: it keeps existing named resources, checks the
resource-group, namespace, DPS, and Hub locations against the requested region,
and creates missing resources. It does not reconcile settings on existing
resources, so review their configuration before reusing them. Namespace links
and enrollment groups with the script's names are also kept if already present.
The default `appsettings.002.json` is created only if it does not already exist;
an existing file is never overwritten. The file is git-ignored and contains a
device-specific symmetric key. Keep it private. Use `-AppConfigPath` to choose
another output path.

## What it creates

In the selected resource group and region, the script creates or reuses:

1. An ADR namespace with a system-assigned identity.
2. A Microsoft-managed root CA, intermediate CA, and certificate policy.
3. Standard S1 DPS and a new standard S1 IoT Hub, both with system-assigned
   identities.
4. The namespace-to-DPS link, then the namespace-to-Hub link, and waits for
   both links to complete.
5. A DPS enrollment group attached to the ADR certificate policy.

No device records, device certificates, or device provisioning actions are
created during setup. The enrollment group is a template for future
provisioning only. The local config uses symmetric-key authentication without
requesting an operational certificate and disables ADR API calls; the sorter
creates the device record in DPS/IoT Hub only when you run it.
Existing resources from another resource group or region are not modified.

## Costs and preview status

IoT Hub and DPS incur their normal S1 charges. Check current Azure pricing and
delete the dedicated test resource group when finished. ADR IoT Hub integration
and certificate management are preview features; do not use this setup for
production workloads.

Deleting the resource group deletes every resource in it. Before cleanup,
confirm it contains only resources you intend to remove; do not delete a group
that also contains existing project resources.

## Verify

The script waits for both namespace links and prints the links and certificate
authorities. You can also inspect the resources in the Azure portal, or run:

```powershell
az iot adr ns link dps list --namespace "<namespace>" --resource-group "<resource-group>"
az iot adr ns link hub list --namespace "<namespace>" --resource-group "<resource-group>"
az iot adr ns ca list --namespace "<namespace>" --resource-group "<resource-group>"
az iot dps enrollment-group list --dps-name "<dps-name>" --resource-group "<resource-group>"
az iot hub certificate list --hub-name "<hub-name>" --resource-group "<resource-group>"
az iot adr ns device list --namespace "<namespace>" --resource-group "<resource-group>"
```

The final device-list command should return no devices for a fresh environment.
The ADR namespace-to-DPS and namespace-to-Hub links are the refreshed preview
connections. Do not treat an empty `az iot dps linked-hub list` result as a
missing connection; that command lists the separate classic DPS allocation
links, not the ADR namespace links.

## Run the sorter

After setup, the script prints a `dotnet run` command that uses the generated
local config. Running it starts the sorter and registers the configured
registration ID through DPS, creating its device record. Stop it with
Ctrl+C. For a hardware-free connectivity test, leave the generated mock sensor
and servo settings enabled.
