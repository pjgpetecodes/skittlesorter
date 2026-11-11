# Troubleshooting

Common issues and diagnostics.

## DPS
- Enrollment mismatch or allocation failures
- MQTT connectivity and firewall checks

### Wrong DPS scope or untrusted bootstrap CA

Read `properties.idScope` from the selected DPS service and compare it with
`IoTHub.DpsProvisioning.IdScope`; a similarly named older deployment can have a
different scope. `401002: CA certificate not found` during X.509 attestation
means the bootstrap issuer is not trusted by that DPS enrollment. Compare the
issuer/thumbprint with the verified DPS CA and enrollment group. Use the
matching new device certificate/key and public chain, not just renamed old
files. See [Pi certificate setup](Quickstart.md#raspberry-pi-update-and-provision).

### AttestationCertPath does not exist

The configuration was copied, but the referenced private device files may not
have been transferred. Git ignores these files. Copy only the device
certificate, its key, and public issuer chain to the configured locations.
Tokenized paths expand `{RegistrationId}`; a suffix such as `-002` is a filename
convention, not a different device ID. Do not copy CA private keys.

### Assigned successfully, then certificate parsing fails

`Assigned` and an assigned Hub show that DPS registration succeeded. A later
`CryptographicException` about a missing PEM `CERTIFICATE` label is a separate
operational-certificate loading failure, not an assignment failure.

Check `git branch --show-current` and `git rev-parse --short HEAD` on both
machines. Older branches used a different certificate-saving path. Current
`main` uses `DpsInitializationService.InitializeAsync` and
`DeviceProvisioningIdentity`, decodes returned base64 DER or base64 PEM
certificates, and validates the issued leaf against the persisted CSR key
before saving it. A stack trace calling the legacy synchronous `Initialize`
does not match this implementation.

Follow the [safe Pi update procedure](Quickstart.md#raspberry-pi-update-and-provision),
then run `dotnet run -- --cloud-self-test`. Preserve the bootstrap files and
matching CSR/private-key pair; do not delete keys to work around a parse error.
If the current code still fails, inspect the exact issued-chain file and any
saved assignment reported by the error, without sharing private keys. Do not
reset working cloud resources solely because a local certificate cannot load.

## ADR
- Authentication via DefaultAzureCredential
- Permissions at subscription/RG/namespace

### Hub connected, but DefaultAzureCredential fails

`[IoT] Connected` followed by `[ADR] Listing failed` means the device connection
worked but optional ARM management authentication did not. A Pi without Azure
CLI, managed identity, or another configured Azure credential produces this
warning. Either disable `Adr.Enabled` or install Azure CLI and sign in as the
app's user; see [ADR authentication](ADR-Integration.md#authentication-on-a-raspberry-pi).
Do not change the DPS scope or device certificate to fix an ARM login error.

### Devices appear in CLI but not the portal resource-group overview

ADR devices are ARM child resources of type
`Microsoft.DeviceRegistry/namespaces/registryDevices`. Inspect the namespace's
**Devices** page as well as the resource-group overview. Check the selected
subscription, filters, and refresh before assuming a provisioning failure.

```powershell
az resource list --subscription "<subscription-id>" `
  --resource-group "<resource-group>" `
  --query "[].{Name:name,Type:type,Id:id}" --output table
az iot adr ns device list --subscription "<subscription-id>" `
  --resource-group "<resource-group>" --namespace "<namespace>" `
  --query "[].{Name:name,State:properties.provisioningState}" --output table
```

For a second inventory view, install the `resource-graph` extension if needed:

```powershell
az extension add --name resource-graph
az graph query --subscriptions "<subscription-id>" --first 100 `
  -q "Resources | where resourceGroup =~ '<resource-group>' | where type =~ 'microsoft.deviceregistry/namespaces/registrydevices' | project name, id, provisioningState=tostring(properties.provisioningState)" `
  --query data --output table
```

Resource Graph can lag live ARM state. A resource returned by ARM with
`provisioningState: Succeeded` is not missing simply because an overview omits
it. The IoT Hub/ADR integration is in preview; a portal display limitation or
bug is possible, but this discrepancy alone does not establish a Microsoft bug
or justify deleting resources. A device named `skittlesorter-002` may be an
earlier test registration; the certificate filename suffix does not create an
additional device automatically.

## Hardware
- Sensor readings drift; re-calibrate
- Servo angles misaligned; re-tune

## Related

- [DPS Provisioning](./DPS-Provisioning.md)
- [ADR Integration](./ADR-Integration.md)
- [Security](./Security.md)