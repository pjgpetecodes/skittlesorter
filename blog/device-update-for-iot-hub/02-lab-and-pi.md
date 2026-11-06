# Part 2: A separate update lab and the right Raspberry Pi payload

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. Draft: actual OS/package compatibility and Azure execution are unverified.

A Raspberry Pi 4 can run 32-bit or 64-bit userland. Detect that before building:

```bash
cat /etc/os-release
dpkg --print-architecture
getconf LONG_BIT
uname -m
```

Debian `arm64` userland maps to `linux-arm64`; `armhf` maps to `linux-arm`. Kernel architecture alone is insufficient. Check the detected OS against both the .NET 10 requirements and the selected Device Update agent package. A Debian base appearing in the service support table does not mean every Raspberry Pi OS image is Microsoft-validated. The support page and GitHub release list currently identify different latest agent versions; pin the actual tested package, not just this article's research baseline.

## Create, rather than repurpose

Install Azure CLI 2.73.0+ and explicitly install the preview Azure IoT extension on the operator computer. Record the actual versions:

```powershell
az extension add --name azure-iot --version 1.0.0b2 --allow-preview true
az version
az extension show --name azure-iot --query version
```

The `scripts\updates\device-update\new-lab.ps1` script takes subscription, new resource-group name, region, Hub, DPS, Device Update account/instance, storage name and operator object ID. Without `-Apply` it only describes the operation. Read its parameters before executing; creating resources costs money. It refuses to reuse an existing resource group.

The script creates an individual symmetric-key enrollment, links standard DPS to the Hub, grants the update account identity Hub data access and grants the operator Device Update Administrator. It does not distribute an enrollment-group master key. It creates a private `updates` container; account-key staging later requires suitable management permissions. Allow RBAC propagation before testing.

The demo Hub enables shared access policies so Azure IoT Explorer can list devices. The GA lab is an exception to the certificate-based ADR demonstration: its setup uses a Hub connection string to link DPS and a device-specific symmetric key for onboarding, so it isn't X.509 end to end. Protect those credentials and the Hub policy keys. Microsoft's guidance warns that disabling Hub shared access policies breaks DPS provisioning, so leave them enabled for this lab. Elsewhere, disabling them and using Microsoft Entra ID for service access reduces reliance on shared secrets, but Explorer can then report `DisableLocalAuth`/`IotHubUnauthorizedAccess`.

## Separate identities and configuration

Copy `src\configuration\appsettings.device-update.template.json` to a protected external configuration file. Supply the DPS scope, registration ID and **individual enrollment device key**. Leave mock hardware enabled until wiring, servo ranges and permissions have been verified. Enable telemetry only when ready to provision.

After the sorter has registered, run `initialize-module.ps1` with that assigned device ID. This explicitly creates module `adu` and sets its **module twin** `ADUGroup` tag. Use the module connection string, not the sorter device connection string, in the standard agent's protected configuration.

Install the selected official agent and its script handler using Microsoft's package instructions. Set the agent's compatibility properties to match the import manifest: manufacturer `SkittleSorter`, model `Pi4`, architecture `linux-arm` or `linux-arm64`. Check the **reported** module properties before importing anything. Do not paste credentials into either article, a release archive or source control.

The next step installs our local application service. Azure success and a real Pi smoke test are explicit gates, not results already obtained here.

[Previous](01-architecture.md) | [Next: safe lifecycle](03-safe-application.md)

Sources: [supported platforms](https://learn.microsoft.com/en-us/azure/iot-hub-device-update/support), [agent releases](https://github.com/Azure/iot-hub-device-update/releases).
