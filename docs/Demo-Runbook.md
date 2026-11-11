# Repeatable X.509 provisioning demo

This runbook covers the lifecycle of the dedicated X.509/CSR demo: creating
the Azure lab, generating its bootstrap identity, provisioning the device and
operational certificate, and resetting only the demo state between runs.
Ordinary F5 never resets cloud or local state.

## 1. Prepare and create the Azure lab

Use a **dedicated** resource group for the demo. Azure IoT Hub, DPS, and ADR
preview resources incur charges. Do not use a resource group containing
unrelated services. See the [infrastructure setup guide](../scripts/iot-hub-device-registry/README.md)
for supported regions, Azure CLI/extension prerequisites, roles, and the
resource topology.

Use PowerShell 7+, Azure CLI 2.73+, Python 3.10+, and the preview-enabled
`azure-iot` extension. Sign-in needs permission to create the resources and
role assignments described by the setup guide. The reset operator also needs
the exact Hub registry read/delete and DPS/ADR device-delete permissions; the
setup script can optionally grant the Hub registry role.

Sign in and preview the deployment first:

```powershell
az login
az account set --subscription "<subscription-id>"

.\scripts\iot-hub-device-registry\setup.ps1 `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "skittlesorter-demo-rg" `
  -Location "uksouth" `
  -NamespaceName "skittlesorter-demo-adr" `
  -HubName "skittlesorter-demo-hub" `
  -DpsName "skittlesorter-demo-dps" `
  -EnrollmentId "skittlesorter-demo" `
  -RegistrationId "skittlesorter-demo-device" `
  -AppConfigPath ".\appsettings.x509-demo.json" `
  -BootstrapDirectory ".\certs\x509-demo" `
  -RegistryOperatorObjectId (az ad signed-in-user show --query id -o tsv) `
  -WhatIf
```

Review the subscription, region, resource names, and preview. Then rerun the
same command without `-WhatIf`. `setup.ps1` creates or reuses the ADR
namespace, IoT Hub, DPS, their links and role assignments, enables Hub shared
access policies, and creates the X.509 enrollment and local bootstrap identity.
It does **not** create an ADR device record or run the sorter. The registration
ID is provisioned when the app first runs. The optional registry operator role
is needed for the exact Hub identity checks used by reset; resource-management
Contributor alone is insufficient.

The script writes `appsettings.x509-demo.json` and the bootstrap files under
`certs\x509-demo\<enrollment>\`. Both locations are ignored by Git. Protect
the bootstrap private keys; this self-signed demo CA is not for production.
The config contains the DPS ID scope needed by the local target manifest.

## 2. Configure the local demo target

Copy the safe template and fill it with the same IDs used in the infrastructure
setup. Use `DpsProvisioning.IdScope` from the generated app config for
`DpsIdScope`, and set `IssuerDpsCertificateName` to
`<EnrollmentId>-bootstrap-ca`:

```powershell
Copy-Item .\scripts\demo-target.template.json .\scripts\demo-target.local.json
notepad .\scripts\demo-target.local.json
```

`demo-target.local.json` is ignored by Git. It contains only target identifiers
and paths, not credentials or private keys. Keep the enrollment and registration
IDs dedicated to this demo; the full-bootstrap reset refuses to delete an
enrollment group containing any registration other than the configured device.

## 3. Understand the two certificate layers

The **bootstrap identity** authenticates the device to DPS. The setup script
creates a demo root/intermediate CA and a device bootstrap certificate/key,
registers the intermediate as a verified DPS CA, and creates the X.509
enrollment group. These files and the enrollment are retained by the first two
reset levels.

On the first F5 run, the app generates a CSR and a separate operational private
key when those files do not exist. DPS/ADR issues the operational certificate
chain from that CSR. The app verifies the returned certificate matches the CSR
key, persists the CSR/key and issued chain, and saves the assigned connection
context in the `.connection.json` sidecar. The issued operational certificate
is the Hub identity; it is distinct from the bootstrap certificate used for
DPS attestation.

Keep the CSR, key, issued chain, and sidecar together to demonstrate reconnect
and persistence. Restarting F5 with those files in place reuses the operational
identity; it does not demonstrate creation of a new certificate.

## 4. Provision the device

In VS Code, choose **SkittleSorter X.509 Demo
(appsettings.x509-demo.json)** in Run and Debug and press F5. F5 builds and
runs only; it never deletes or recreates anything. Keep the generated mock
hardware enabled for a hardware-free demonstration.

To run from a terminal instead:

```powershell
dotnet run --project .\skittlesorter.csproj -- --config .\appsettings.x509-demo.json
```

On a clean bootstrap setup, show the CSR/key creation, DPS assignment, ADR
device creation, and operational certificate issuance. The registration
appears in DPS, the ADR namespace, and the assigned Hub. Stop the app with
Ctrl+C. Starting it again without resetting local files demonstrates reuse of
the saved connection context and operational certificate.

Inspect the target state with:

```powershell
az iot dps enrollment-group registration show `
  --dps-name "<dps-name>" --resource-group "<resource-group>" `
  --registration-id "<device-id>"
az iot adr ns device list --namespace "<namespace>" --resource-group "<resource-group>"
az iot hub device-identity show `
  --hub-name "<hub-name>" --resource-group "<resource-group>" `
  --device-id "<device-id>" --auth-type login
```

These are real Azure operations and may incur costs. For a bounded, hardware-free
connectivity check, use `--cloud-self-test` with the same config; it provisions,
waits for one telemetry acknowledgement, and exits.

## 5. Reset for a repeat demo

Stop the sorter before resetting. The three reset levels deliberately separate
cloud reprovisioning from replacing certificates:

| Scenario | Deletes | Keeps |
|---|---|---|
| `CloudOnly` | Exact ADR device, Hub identity, DPS registration, and cached connection sidecar | Bootstrap identity, CSR/key, issued operational chain, enrollment, all Azure resources |
| `OperationalIdentity` | Everything in `CloudOnly`, plus the CSR, operational private key, and issued chain | Bootstrap identity and verified issuer, enrollment, all Azure resources |
| `FullBootstrap` | Exact device state, the dedicated DPS enrollment and verified bootstrap issuer, dedicated bootstrap/runtime certificate files, and `appsettings.x509-demo.json` | Resource group, Hub, DPS, ADR namespace, shared ADR CA/policy, target manifest, and unrelated profiles/files |

Every reset is bound to the local target file and prompts before changes. Preview
the exact target first:

```powershell
.\scripts\demo-reset.ps1 -Scenario CloudOnly -WhatIf
.\scripts\demo-reset.ps1 -Scenario OperationalIdentity -WhatIf
.\scripts\demo-reset.ps1 -Scenario FullBootstrap -WhatIf
```

Run the selected reset from a terminal with `-Confirm`, or in VS Code open
Command Palette (**Ctrl+Shift+P**) → **Tasks: Run Task** and choose the matching
`Demo:` task. Each reset has a separate preview task. The reset scripts use the
same exact-device checks and reset implementation as the terminal workflow;
the tasks do not duplicate deletion logic.

### Repeat the provisioning demo

- For another DPS/ADR/Hub provisioning sequence using the **same** operational
  identity, run `CloudOnly`, then press F5.
- To demonstrate **new CSR and operational-certificate issuance** while keeping
  the bootstrap CA and enrollment, run `OperationalIdentity`, then press F5.
- To demonstrate **bootstrap certificate and enrollment creation**, run
  `FullBootstrap`, preview and confirm it, then use
  `scripts\demo-setup.ps1 -WhatIf` followed by
  `scripts\demo-setup.ps1 -Confirm`. In VS Code use **Demo: Set up X.509
  bootstrap (after full reset)**, which also prompts; preview it first with
  **Demo: Preview X.509 bootstrap setup**. Then F5 generates a new CSR/key,
  provisions the device, and requests a new operational certificate.

The bootstrap setup command/task recreates only the dedicated demo issuer,
enrollment, local bootstrap credentials, and config from the safe application
template. It does not create Azure infrastructure. If the resource group, Hub,
DPS, or ADR namespace was removed, rerun the infrastructure setup in section 1
first. Shared ADR CA/policy and all Azure infrastructure are intentionally
outside the full-bootstrap reset.

Do not use the broad `remove-resources.ps1` cleanup script for these demos; it
can delete resource infrastructure and certificate directories. To stop
ongoing Azure charges, separately inspect the dedicated resource group and
delete it only when you intend to remove every resource in it.
