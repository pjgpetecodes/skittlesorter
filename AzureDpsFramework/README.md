# Azure DPS Framework: unified SDK adapter

## What's changed?

`AzureDpsFramework` now wraps Microsoft's unified
[`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4)
on .NET 10. The official SDK supports the CSR certificate-issuance flow that
originally motivated this project's custom provisioning implementation.

The former C# security providers, provisioning transports, registration protocol,
and SAS-token implementation are removed. `ConnectionClient` handles DPS
provisioning and the IoT Hub connection; feature clients such as `TelemetryClient`
share that connection. Existing code built around the old framework's provider
and transport APIs must migrate rather than merely changing package versions.

Read Microsoft's [SDK design and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified)
alongside the adapter source. The SDK and ADR certificate-management integration
are preview features, not a production-readiness guarantee.

## Why keep this library?

It remains the boundary between the sorter and application-specific Azure setup:

- Load/validate DPS configuration and select the existing authentication profile.
- Accept a device-specific symmetric key, with group-key derivation retained for
  compatibility; do not distribute group master keys unnecessarily.
- Generate/load CSRs and private keys and persist the issued certificate chain.
- Adapt those inputs through `UnifiedDeviceSession` into a unified SDK session.
- Keep optional Azure Device Registry ARM metadata queries and updates separate
  from device-protocol operations.

ADR ARM metadata uses `namespaces/registryDevices` with API version
`2026-11-02-preview`, not the retired `/devices` resource path.
Its writable properties are `enablementState` (`Enabled`/`Disabled`),
`manufacturer`, `model`, `hardwareRevision` and `softwareRevision`.
`UpdateDeviceAsync` retains configuration compatibility: `DeviceEnabled` maps
to `enablementState`, and `OperatingSystemVersion` maps to `softwareRevision`
(overriding `Attributes.softwareRevision` if both are supplied). `Attributes`
accepts only the four exact lower-camel-case metadata names above; arbitrary keys
are rejected explicitly. Use string-valued resource `Tags` for custom labels
such as hardware ID, owner, location and device type.

It does **not** implement DPS MQTT topics, response correlation, registration
polling, or SAS-token authentication itself. Those responsibilities belong to
the official SDK. Don't copy the removed protocol types back into an application.

## Supported profiles

| DPS attestation | `RequestOperationalCertificate` | Hub authentication |
|---|---|---|
| Symmetric key | `false` | Existing device-specific symmetric key |
| X.509 | `false` | Existing certificate/private key |
| Symmetric key + CSR | `true` | Newly issued operational certificate |
| X.509 + CSR | `true` | Newly issued operational certificate |

`RequestOperationalCertificate` defaults to `true` for compatibility with the
original CSR examples. Set it explicitly to `false` for standard DPS profiles,
including the GA Device Update example. `DeviceKeyBase64` supplies a device key
directly; `EnrollmentGroupKeyBase64` is the group-key compatibility alternative.

In CSR profiles, the onboarding key or bootstrap certificate authenticates to
DPS. A separate device-owned key signs the CSR; the issued leaf certificate and
chain are then used for IoT Hub. With issuance disabled, no operational
certificate should be requested or required.

## Configuration and durable state

Start with the safe templates under
[`src/configuration`](../src/configuration/) and the full
[configuration reference](../docs/Configuration.md).

Keep these files outside versioned application releases:

- `CsrFilePath`: the generated or supplied CSR.
- `CsrKeyFilePath`: its matching private key.
- `IssuedCertFilePath`: the issued leaf and certificate chain.
- The `.connection.json` assignment sidecar alongside the issued PEM: enough
  assignment state to restore the operational certificate and assigned Hub on restart.
- For X.509 onboarding, `AttestationCertPath`, `AttestationKeyPath`, and
  `AttestationCertChainPath`.

Paths support `{RegistrationId}`. Relative paths retain the repository
compatibility rule: use the nearest project root above the selected config when
one exists, otherwise the config's directory. Absolute paths are unchanged.
Reuse matching CSR/key files across retries and restarts;
do not overwrite a private key while retaining a certificate issued for the
old key. Persist the full issued chain rather than just the leaf. The private
key remains local and must never be logged, uploaded, or committed.

`ProvisioningLockPath` can coordinate provisioning with the separate ADR updater;
both processes must use the same path when sharing an identity. A lock does not
make it safe to run two update controllers.

The session coordinator delegates all MQTT operations to the SDK. It holds the
shared updater lock only during DPS sessions, including SDK reprovisioning, not
throughout the Hub connection lifetime. A required-certificate wrapper rejects a
missing issued chain before Hub authentication rather than silently falling back
to the onboarding credential in a CSR profile.

## Connection and certificate lifecycle

The SDK provides retry/reconnection and reprovisioning behavior through
`ConnectionClient`. Configure and observe that behavior through the SDK rather
than introducing a second application retry loop for the removed transport.

The unified SDK also exposes optional IoT Hub certificate-renewal APIs.
**Automatic operational-certificate renewal is not implemented by this adapter
or the sorter.** Renewal scheduling, durable replacement and recovery require
their own validated lifecycle design. Deleting a certificate is not an
implementation of the Hub renewal API.

This migration keeps the existing IoT Hub and cloud topology. The SDK discovers
the assigned endpoint and connection profile from DPS; don't infer a hostname
from the Hub name or force an MQTT version in the adapter. The sorter's MQTTnet
dependency moves to **5** with the SDK; that dependency version does not select
or migrate the Hub protocol.

## Bounded validation

From the repository root:

```powershell
# Offline configuration validation
dotnet run --project .\skittlesorter.csproj -- --self-test --config .\appsettings.json

# Real provisioning, connection, representative telemetry and graceful disconnect
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

The cloud self-test has a two-minute bound, waits for one representative telemetry
acknowledgement, and never initializes physical hardware. It requires real Azure
credentials and network access. The operator helper
`scripts\iot-hub-device-registry\test-unified.ps1 -AppConfigPath PATH`
does not reset by default; `-ResetDevice` requires explicit matching resource
names and removes only that exact device/registration, not a resource group,
enrollment group, or CA. See the [runbook](../scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test).

## Diagnostics and troubleshooting

- Check ID scope, registration ID, selected profile and enrollment credentials.
- For X.509, check certificate validity, private-key matching and the onboarding
  chain configured in DPS.
- For CSR issuance, check the selected ADR certificate policy and persisted
  CSR/key pair; verify the returned chain matches that key.
- Check the assigned Hub and outbound TLS connectivity. Do not change cloud
  topology merely to resolve an SDK upgrade issue.
- The SDK uses `System.Diagnostics.Trace`; see the official
  [logging guidance](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md#capturing-logs).
  Old custom `[MQTT]` parsing/polling messages are no longer the logging contract.
  Logs can contain identities, topics and message contents: restrict access and
  redact before sharing.

## Separate software-updates client

`AzureSoftwareUpdatesFramework` and `tools/SkittleSorter.Updater` remain separate.
Their custom MQTT implementation is for **DPS software updates**, not provisioning.
Microsoft's unified .NET SDK does not implement that workflow. The updater
retains MQTTnet **4**, independently of the sorter's MQTTnet 5 dependency.

The GA Device Update agent and ADR Software Updates controller still require
separate cloud resources and must not run together on one Pi. See
[application updates](../docs/Application-Updates.md).

## NuGet distribution

The adapter retains the `AzureDpsFramework` project/package identity. Packaging
uses the workflows under `.github/workflows`; publishing remains tag-based
(`v<version>`). Older published versions containing custom protocol types are
historical, not an installation shortcut for this migrated API.

For development, consume the adapter project from this solution. If distributing
it separately, publish a version containing the migration and pin that version
in consumers. The Microsoft dependency is pinned to **2.0.0-preview4**; do not
substitute a floating preview reference. For applications that don't need this
repository's configuration/persistence/ADR helpers, use the official package
directly:

```powershell
dotnet add package Microsoft.Azure.Iot.Device --version 2.0.0-preview4
```

GitHub Packages feed: `https://nuget.pkg.github.com/pjgpetecodes/index.json`.
Provide feed credentials securely in local/CI configuration, never in source
control. Existing build/package and tag-publish workflow names remain unchanged.

## Related

- [Architecture](../docs/Architecture.md)
- [DPS provisioning](../docs/DPS-Provisioning.md)
- [X.509 attestation guide](../scripts/X509_ATTESTATION_GUIDE.md)
- [Microsoft certificate-management overview](https://learn.microsoft.com/azure/iot/iot-certificate-management-overview)
