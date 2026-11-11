# DPS Provisioning

Provision devices via DPS using symmetric key or X.509.

## What's changed?

The sorter uses Microsoft's unified [`Microsoft.Azure.Iot.Device`
2.0.0-preview4](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4).
`ConnectionClient` owns registration, authentication, connection retry and
reprovisioning. The sorter calls it directly; `AzureDpsFramework` provides
configuration, durable credentials and optional ADR ARM helpers. Its existing
session API remains for package compatibility, not as the sorter's entry point.

## Profiles

| Attestation | `RequestOperationalCertificate` | IoT Hub credential |
|---|---|---|
| Symmetric key | `false` | Existing device-specific key |
| X.509 | `false` | Existing certificate and private key |
| Symmetric key + CSR | `true` (compatibility default) | ADR-issued operational certificate |
| X.509 + CSR | `true` (compatibility default) | ADR-issued operational certificate |

Prefer `DeviceKeyBase64` over distributing a group master key. In CSR profiles,
the onboarding credential authenticates to DPS, while the separate CSR key belongs
to the operational certificate. Keep `CsrFilePath`, `CsrKeyFilePath`, and
`IssuedCertFilePath` durable across restarts, including the issued chain.
Paths retain the existing resolution rules; see the [configuration reference](Configuration.md).

Keep the `.connection.json` assignment sidecar alongside the issued PEM so the
application can restore the operational certificate and assigned Hub on restart.
CSR profiles reject a missing issued chain before Hub authentication. The
shared updater lock is held only during DPS sessions, including SDK
reprovisioning, not throughout the Hub telemetry connection.

## Connection and lifecycle

The existing Hub and cloud resources stay in place. The SDK discovers the assigned
endpoint and profile through DPS; do not construct the hostname from the Hub name
or force the protocol. MQTTnet 5 is a dependency upgrade, not a Hub migration.
`TelemetryClient` shares the unified connection.
SDK retry/reprovision support is available, as are optional IoT Hub certificate
renewal APIs; this app does **not** implement automatic operational-certificate
renewal. See the [official SDK feature matrix and samples](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md).

The separate `AzureSoftwareUpdatesFramework` implements DPS **software updates**
over its own MQTT protocol and stays on MQTTnet 4. The unified SDK does not
implement that update workflow.

## Bounded cloud validation

```powershell
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

This has a two-minute bound, provisions/connects, waits for one representative
telemetry acknowledgement and disconnects
gracefully without physical hardware. It makes real Azure requests; the
offline alternative remains `--self-test --config PATH`.

## Tips
- Ensure enrollment group alignment with device credentials.
- Check allocation to the target IoT Hub.

## Related

- [Azure Setup](./Azure-Setup.md)
- [Configuration](./Configuration.md)
- [ADR Integration](./ADR-Integration.md)
- [Repeatable X.509 Provisioning Demo](./Demo-Runbook.md)