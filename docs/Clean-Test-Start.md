# Clean Test Start

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
  -AppConfigPath .\appsettings.002.json `
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
run when resetting. Existing local configurations may still use the earlier
symmetric-key profile; `appsettings.x509-002.json` uses X.509 attestation and
CSR issuance. Both may use `skittlesorter-002`; the reset checks that the
specified group owns the current registration. Then select the desired profile.

The older cleanup script used to delete an enrollment group and the entire local
`certs` directory. Those broad deletions are intentionally no longer performed.
Never delete the resource group as part of this device reset.

## Related

- [Azure Setup](./Azure-Setup.md) - Initial Azure resource setup
- [DPS Provisioning](./DPS-Provisioning.md) - DPS provisioning flows
- [Configuration](./Configuration.md) - Configuration reference
- [Quickstart](./Quickstart.md) - Getting started guide
