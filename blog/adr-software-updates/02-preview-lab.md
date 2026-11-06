# Part 2: Prepare an independent ADR Software Updates environment

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft: resource creation and real authentication remain unverified.

Do not run the old certificate-management setup blindly. Current guidance creates a standard S-tier Hub, connects DPS first, then connects the Hub and optional software-update instance to the namespace. Existing eligible resources can be connected, but linking is irreversible. This demonstration intentionally creates only new resources.

## Operator setup

Use Azure CLI 2.73.0+ and explicitly install the pinned Azure IoT preview extension:

```powershell
az extension add --name azure-iot --version 1.0.0b2 --allow-preview true
az extension show --name azure-iot --query version
```

Read `scripts\updates\adr\new-lab.ps1`. Supply an explicit subscription, new resource group, supported common region, namespace, DPS, separate Hub, update-instance name, storage name and operator object ID. Default execution is descriptive only; `-Apply` creates billable resources.

The script enables system-assigned identities, uses `az iot adr ns link dps add` before `link hub add`, creates the update instance with `az iot adr ns su instance create`, links it using `link su add` and waits for links. It creates private artifact storage and operator content/storage roles.

The link workflow handles required service-to-service grants when the operator can create role assignments. That is different from granting the signed-in operator content permissions. Inspect all links and allow identity/RBAC propagation. Confirm the Hub has **no GA Device Update instance**.

## X.509 and the first proof

The custom update channel requires a registered device and supported X.509 authentication. Do not copy the GA symmetric-key profile and assume update checks support it. Use a valid enrollment/bootstrap chain following the pinned regular-update sample and current DPS instructions; verify certificate identity, private-key matching, trust and assignment.

The sorter's provisioning now uses the unified `Microsoft.Azure.Iot.Device`
**2.0.0-preview4** adapter, preserving CSR/key, issued PEM/chain and assignment
sidecar files. That doesn't replace the software-update client or prove this
separate environment's X.509 update check/report path. Automatic operational-
certificate renewal is not implemented.

Store app configuration, updater configuration, device certificates and trust anchors outside releases. Both the sorter provisioning profile and updater must use `/var/lib/skittlesorter/provisioning.lock` or another identical, appropriately permissioned path.

Before any job, prove a real authenticated check and accepted report against the target service/API. Check the `2026-11-02-preview` contract is actually available in that environment. A mocked MQTT test or a successful namespace link does not establish that. This is currently an outstanding gate.

[Previous](01-architecture.md) | [Next: custom C# channel](03-custom-client.md)

Sources: [current setup](https://learn.microsoft.com/en-us/azure/iot/device-registry/get-started-azure-device-registry), [pinned regular-update sample](https://github.com/Azure/azure-iot-sdk/blob/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/samples/software_update/pc/simulated_regular/README.md).
