# Clean Test Start

For the complete infrastructure-to-certificate-to-provisioning-to-reset demo
cycle, use the [Repeatable X.509 Provisioning Demo runbook](./Demo-Runbook.md).
The VS Code Command Palette resets and standalone scripts there target only a
dedicated local demo configuration.

Use the exact-device reset before a demo when you want the configured device to
provision again from DPS. Stop the sorter first. The reset keeps the resource
group, IoT Hub, DPS, ADR namespace, enrollment group, CA certificates, and local
CSR/key/certificate files. It removes only the configured device's ADR record,
Hub identity, DPS registration, and cached `.connection.json` assignment.

The script requires the explicit Azure resource names and verifies the DPS
scope, enrollment group, namespace, assigned Hub, and registration ID before it
deletes anything. It checks the exact Hub identity first; management-plane
Contributor access alone is not enough. For a Hub with local authentication
disabled, use Entra authentication and the required Hub data-plane registry
permissions. The script stops without making changes if that check fails.
If ADR deletion also removes the Hub identity, the reset handles that cascade;
an interrupted reset can be resumed while the DPS registration remains assigned.

From the repository root:

```powershell
.\scripts\clean-test-start.ps1 `
  -AppConfigPath .\appsettings.json `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroup "my-iot-preview-rg" `
  -DpsName "my-iot-dps-002" `
  -IotHubName "my-iot-hub-002" `
  -EnrollmentGroupName "my-enrollment-group-002" `
  -DeviceId "skittlesorter-002" `
  -AdrNamespace "my-iot-adr-namespace-002"
```

Use `-WhatIf` to confirm the exact target without running the reset. The script
prompts before deletion. It is a reset-only operation; after it succeeds, start
the app with the same configuration to demonstrate fresh provisioning. To reset
and immediately run the bounded connectivity check instead, use
`scripts\iot-hub-device-registry\test-unified.ps1 -ResetDevice` without
`-ResetOnly`.

For F5 demo profiles, use the config and enrollment group of the **previous**
run when resetting. The default `appsettings.json` is the certificate-based
profile; optional symmetric-key and X.509 profiles can be stored in
`appsettings.sas.json` and `appsettings.x509.json`. The reset checks that the
specified group owns the current registration before changing device state.

The older cleanup script used to delete an enrollment group and the entire local
`certs` directory. Those broad deletions are intentionally no longer performed.
Never delete the resource group as part of this device reset.

## Progress and deletion latency

After confirmation, the script performs several sequential Azure CLI preflight
requests before deleting anything. CLI startup, authentication, network retries,
and service response times contribute to the overall duration. The ADR device
can therefore remain visible while the script is still checking its target.

Each Azure operation now prints its start time and elapsed duration. The last
started operation shows where the script is waiting. ADR deletion is submitted
with `--no-wait`, followed by one explicit `--deleted --timeout 120` wait before
Hub/DPS cleanup continues. Previously the delete command itself also waited for
Azure's long-running operation before the explicit wait began. The 120-second
wait is not a timeout for the entire reset or every CLI/network request.

Portal refresh can lag deletion; use CLI inventory to confirm the resulting
state. Do not launch a second reset while the first is running. If an operation
fails or times out, inspect ADR, Hub, and DPS state before retrying; deleting ADR
does not by itself prove the remaining reset steps completed.

## Multiple demo identities and authentication failures

`clean-test-start.ps1` resets **one** device, whose `DeviceId` must match
`RegistrationId` in the selected config. Repeat with a matching profile for each
test identity you intentionally want to remove. For example, the PC
`skittlesorter`, Pi `skittlesorter2`, and an older `skittlesorter-002` test are
separate records; never infer a registration ID from a certificate's suffix.
Leave a device absent after reset until you intentionally run its app or cloud
self-test; those commands reprovision it.

If the script reports Hub `Unauthorized` during its preflight, no deletion has
started. The script defaults to `--auth-type login` for Hub registry access.
An ARM Contributor role or a successful key-based CLI list does not prove that
the operator has the required Entra Hub registry data-plane permissions. Grant
the appropriate narrowly scoped registry role through an authorized
administrator, then rerun the guarded script.

For a demo Hub with local authentication enabled, an authorized operator may
instead rerun the guarded reset with `-HubAuthType key`. Both
`clean-test-start.ps1` and `iot-hub-device-registry\test-unified.ps1` accept this
explicit option and apply it to Hub identity preflight, cascade checks, and
deletion. All existing scope/identity checks remain. There is no automatic
fallback from a failed login. Key mode requires permission to retrieve a Hub
policy key with registry access; it does not require printing or copying a
connection string and will not work when Hub local authentication is disabled.

For manual recovery of an interrupted reset, inspect the exact DPS registration and verify its
device ID, enrollment membership, and assigned Hub, then inspect the matching
ADR and Hub records:

```powershell
az iot dps enrollment-group registration show --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --dps-name "<dps-name>" `
  --registration-id "<device-id>" --auth-type key
az iot dps enrollment-group registration list --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --dps-name "<dps-name>" `
  --enrollment-id "<enrollment-id>" --query "[].registrationId" --auth-type key
az iot adr ns device show --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --namespace "<namespace>" --name "<device-id>"
az iot hub device-identity show --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --hub-name "<hub-name>" `
  --device-id "<device-id>" --auth-type key --query deviceId
```

Only after those checks, stop the app and delete that exact ADR device:

```powershell
az iot adr ns device delete --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --namespace "<namespace>" --name "<device-id>" --yes
az iot adr ns device wait --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --namespace "<namespace>" --name "<device-id>" --deleted --timeout 120
az iot hub device-identity list --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --hub-name "<hub-name>" `
  --auth-type key --query "[].deviceId"
```

Stop if any command fails. ADR deletion can also remove the Hub identity. Run
the following Hub deletion **only if the exact ID remains in the successful
list above**, then remove its DPS registration:

```powershell
az iot hub device-identity delete --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --hub-name "<hub-name>" `
  --device-id "<device-id>" --auth-type key
az iot dps enrollment-group registration delete --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --dps-name "<dps-name>" `
  --registration-id "<device-id>" --auth-type key
```

Finally remove only that profile's cached assignment: the resolved
`IssuedCertFilePath` plus `.connection.json`, expanding `{RegistrationId}`.
Keep the bootstrap certificate/key, CSR/key, and issued operational chain.
Repeat the inventory commands to confirm the exact device is absent from ADR,
Hub, and DPS, and that the enrollment and infrastructure remain. Do not start
the cloud self-test merely to verify absence: it creates the device again.

## Related

- [Azure Setup](./Azure-Setup.md) - Initial Azure resource setup
- [DPS Provisioning](./DPS-Provisioning.md) - DPS provisioning flows
- [Configuration](./Configuration.md) - Configuration reference
- [Quickstart](./Quickstart.md) - Getting started guide
