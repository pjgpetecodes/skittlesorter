# Azure IoT Hub + Device Registry preview setup

`setup.ps1` provisions a new IoT Hub, DPS, and Azure Device Registry
certificate-management environment using the refreshed public-preview CLI
workflow. It creates a self-signed demo bootstrap CA, an X.509 enrollment and
a local device certificate, and writes a CSR-enabled sorter configuration. It does not create device records or
provision a device until the sorter itself is run.

For a complete repeatable demonstration, including clean reset choices, see
the [X.509 provisioning demo runbook](../../docs/Demo-Runbook.md).

## What's changed?

The generated configuration works with the sorter's unified
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** adapter on .NET 10. Setup now
generates a CSR-enabled X.509 onboarding profile
(`RequestOperationalCertificate: true`) with durable CSR/key/issued-chain paths.
Set that option explicitly to `false` for standard symmetric-key or X.509
provisioning without issuance. Both X.509 profiles are also retained.
There is no manual Hub or Azure resource topology migration. The SDK discovers
the assigned endpoint/profile through DPS; don't construct the hostname from a
Hub name or force a protocol.

## Prerequisites

- Azure CLI 2.73.0 or later and PowerShell 7 or later.
- Python 3.10 or later for the Azure IoT CLI extension.
- An Azure subscription and permission to create resources and role assignments
  at the required scopes (for example, Owner, or Contributor plus User Access
  Administrator).
- A supported preview region: `australiaeast`, `japaneast`, `centralus`, or
  `uksouth`.

The optional exact-device reset also needs **IoT Hub data-plane registry
read/delete permissions** for the signed-in CLI principal, for example an
appropriately scoped **IoT Hub Registry Contributor** role. Resource-management
Contributor access alone is not sufficient. If the Hub has
`DisableLocalAuth=true`, use Microsoft Entra authentication
(`--auth-type login`); SAS/shared-access-policy authentication is disabled.
Ensure the operator has the required DPS registration and ADR device-delete
permissions as well. To request a Hub-scoped registry role assignment during
setup, supply the operator's Entra object ID with `-RegistryOperatorObjectId`.
This is opt-in and requires permission to create role assignments. Use
`-RegistryOperatorPrincipalType ServicePrincipal` or `Group` if appropriate;
the default is `User`. Existing exact-scope assignments are retained. The
assignment is separately confirmed and can take several minutes to propagate.

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

To grant the signed-in user registry access for demo resets, add
`-RegistryOperatorObjectId (az ad signed-in-user show --query id -o tsv)` to
the setup command. Do not pass an application ID in place of the Entra object ID.

The script is resumable: it keeps existing named resources, checks the
resource-group, namespace, DPS, and Hub locations against the requested region,
and creates missing resources. It enables Hub shared access policies, including
on an existing Hub, so IoT Explorer and shared-access-based service clients can
connect. This allows Hub policy keys to authenticate service clients; protect
the keys and use a dedicated demo Hub. Azure Policy can block or override this
setting, in which case setup stops. Other settings on existing resources are not
reconciled, so review their configuration before reusing them. Namespace links
are kept if already present, and setup checks that they target the requested
DPS and Hub. The default `appsettings.002.json` is created only
if it does not already exist. Existing configs and enrollments are never
overwritten: a reused config must match the X.509 enrollment, issuer, DPS scope
and ADR policy. For an older symmetric-key config or enrollment, choose a new
`-AppConfigPath` and enrollment ID. If an interrupted first run left bootstrap
files or an enrollment without its config, inspect them before retrying; setup
will not silently replace them.

The self-signed bootstrap CA and device key are stored under the ignored
`certs\x509-bootstrap` directory. This CA is only for demonstrations: its
private key must be protected and must not be used in production. It is
separate from the Microsoft-managed ADR operational issuer. Setup marks the
demo CA verified in DPS, establishing trust for certificates it issues.
The generated profile also keeps its CSR, operational key, issued chain and
assignment sidecar under that enrollment's device directory, separate from
other profiles. Preserve these files across restarts.

### Additional X.509 profile with an existing issuer

To add another X.509 enrollment for the same demo device using an existing
intermediate CA, provide its certificate, private key and full issuer chain:

```powershell
.\scripts\iot-hub-device-registry\setup-x509-demo.ps1 `
  -SubscriptionId "<subscription-id>" -ResourceGroupName "my-iot-preview-rg" `
  -DpsName "my-iot-dps-002" -NamespaceName "my-iot-adr-namespace-002" `
  -EnrollmentId "my-x509-enrollment-002" `
  -IssuerCertificatePath "<intermediate-ca.pem>" `
  -IssuerKeyPath "<intermediate-ca.key>" -IssuerChainPath "<issuer-chain.pem>" `
  -WhatIf
```

After reviewing the preview, rerun without `-WhatIf`. This registers the supplied
issuer as a **verified** DPS CA (self-attestation), creates an X.509 enrollment,
and creates an ignored `appsettings.x509-002.json` with an ignored bootstrap
key and certificate under `certs\x509-demo`; existing files or an existing
enrollment are never overwritten. Trust only a CA you control: verified DPS CAs
establish trust for certificate-based enrollment. Protect the issuer and device private keys.
The profile defaults to X.509 attestation and requests an operational
certificate using a CSR. Pass `-RequestOperationalCertificate $false` only for
standard X.509 provisioning without issuance. The issuer chain must start with
the supplied intermediate CA.
Reset the device registration from the **previous** enrollment before
switching profiles; see [Clean Test Start](../../docs/Clean-Test-Start.md).

## What it creates

In the selected resource group and region, the script creates or reuses:

1. An ADR namespace with a system-assigned identity.
2. A Microsoft-managed root CA, intermediate CA, and certificate policy.
3. Standard S1 DPS and a new standard S1 IoT Hub, both with system-assigned
   identities.
4. The namespace-to-DPS link, then the namespace-to-Hub link, and waits for
   both links to complete.
5. A DPS enrollment group attached to the ADR certificate policy.

No cloud device records or provisioning actions are created during setup. The
local bootstrap device certificate is created for X.509 DPS attestation; the
enrollment group is a template for future provisioning. The config requests an
operational certificate through the ADR policy. ADR ARM
metadata calls remain disabled; the sorter creates the device record and obtains
the operational certificate only when you run it.
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

## Bounded unified SDK cloud test

For a finite connectivity check, use the generated configuration directly:

```powershell
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.002.json

# Operator helper: no reset by default
.\scripts\iot-hub-device-registry\test-unified.ps1 `
  -AppConfigPath .\appsettings.002.json
```

The cloud self-test has a two-minute bound, bypasses physical hardware,
provisions/connects through the unified SDK, waits for one representative telemetry
acknowledgement, then disconnects gracefully. This is
an online test with real Azure credentials and resource costs, not the offline
`--self-test --config PATH`. A successful local build or offline test is not
evidence of a successful cloud run.

For a fresh-registration test, first stop the sorter and review the exact
subscription/resource group, Hub, DPS, namespace, enrollment and identity. The
helper accepts an explicit `-ResetDevice` only with the matching resource names:

```powershell
.\scripts\iot-hub-device-registry\test-unified.ps1 `
  -AppConfigPath .\appsettings.002.json `
  -ResetDevice `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "my-iot-preview-rg" `
  -DpsName "my-iot-dps-002" `
  -EnrollmentId "my-enrollment-group-002" `
  -HubName "my-iot-hub-002" `
  -NamespaceName "my-iot-adr-namespace-002"
```

It resets **only the configured exact device/registration**, never the resource
group, enrollment group, or CA. Add `-ResetOnly` to leave the device unprovisioned
for a demo instead of immediately running the cloud test. The root
`scripts\clean-test-start.ps1` is a guarded wrapper for this reset-only mode.
Protect and retain CSR/key and issued-chain files unless deliberately resetting
that identity. Keep the
`.connection.json` assignment sidecar alongside the issued PEM so restarts can
restore the operational certificate and assigned Hub.

Before any destructive reset, the helper checks the exact Hub identity using
the operator's Entra login. If that read fails for a reason other than the
identity being absent, stop and resolve authentication or registry RBAC;
don't fall back to SAS on a Hub with local authentication disabled. If ADR
deletion cascades to the Hub identity, the helper detects its absence and
continues. A successful device-authenticated telemetry test does not establish
operator permissions to read or delete Hub identities.

The unified SDK provides retry/reprovision and optional IoT Hub certificate-renewal
APIs, but this test does not implement automatic renewal or validate the separate
ADR Software Updates protocol. See Microsoft's
[SDK design/feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).

### Validation status — 6 November 2026

- A dedicated resource group was deleted and recreated with the full setup
  script, using a fresh config and bootstrap directory. Both namespace links
  succeeded, Hub shared access policies were enabled, and the X.509 enrollment
  referenced the ADR certificate policy. Before provisioning, the namespace
  had no devices. A subsequent setup run retained the resources and verified
  the namespace-link targets and existing enrollment.
- The generated profile completed a fresh CSR cloud self-test, with a matching
  key and a three-certificate issued chain, and a second process reconnected
  with the persisted assignment and acknowledged telemetry. The normal debug
  profile's executable and arguments also connected with mock hardware and
  completed a sort. These commands were run directly, not through an attached
  VS Code debugger.
- The existing X.509 enrollment completed a fresh CSR cloud self-test, with a
  newly issued certificate matching the generated key and an acknowledged Hub
  telemetry message. The X.509 F5 profile was then configured for CSR and
  passed the same bounded cloud test. A newly generated demo bootstrap CA and
  X.509 enrollment also completed CSR issuance, telemetry and a reconnect
  against the then-existing demo environment. Only the temporary test device,
  enrollment and CA were removed afterward. That earlier check did not test a
  new resource group or the entire lab-creation script against Azure.
- Standard unified symmetric-key provisioning, Hub connection, one telemetry
  acknowledgement and graceful disconnect passed against Azure.
- All 17 sorter and 17 updater unit tests passed; the build completed without
  warnings. The published Linux process-lifecycle test passed in Ubuntu WSL on
  its native filesystem, not on physical Raspberry Pi hardware.
- CSR cloud self-test passed: the device provisioned and connected using its
  required operational certificate, telemetry was acknowledged, and it
  disconnected gracefully. The issued PEM was persisted locally. Certificate
  chain normalization resolved the earlier malformed-PEM preparation error;
  that failure was not evidence of an Azure issuance problem.
- The persisted operational chain was verified with subject
  `CN=skittlesorter-002` and managed intermediate/root certificates. DPS
  assignment and a new ADR UUID confirmed recreation of the device.
- Exact ADR/DPS device records were reset and recreated; no resource-group
  deletion was needed. A second process restored the saved assignment and
  operational certificate, reconnected, and received a cloud telemetry
  acknowledgement. Final validation passed all 34 .NET tests and the published
  Linux SIGTERM lifecycle check.
- At the time of this validation the operator lacked Hub data-plane registry
  access, so the optional full manual Hub-identity reset was not tested.
