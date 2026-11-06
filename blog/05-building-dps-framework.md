# Keeping a Thin DPS Adapter with the Unified SDK

> **SDK baseline:** `Microsoft.Azure.Iot.Device` **2.0.0-preview4**, on
> **.NET 10**. The Azure IoT CLI examples still use `azure-iot` **1.0.0b2**.

[← Previous: Configuring Enrollment Groups](04-configuring-enrollment-groups.md) | [Next: Building the Device Application →](06-building-device-application.md)

---

## What's changed?

This used to be the “building a custom DPS framework” post. I originally wrote
the MQTT registration, security providers and SAS-token handling myself to get
the CSR flow working. Microsoft's unified .NET SDK now covers that provisioning
and connection work, so I've removed those pieces rather than keeping a second
implementation alive.

The filename stays put so existing links still work, but the code examples for
the old custom providers and transports are deliberately gone. They describe a
previous implementation, not APIs you should use with the current repository.

We're pinning [`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4).
It is still a preview. Microsoft's [SDK overview and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified examples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified)
are the useful starting points for the API, rather than my old look-alike classes.

## Why keep AzureDpsFramework?

There's still some useful glue here! The sorter has existing configuration
profiles, certificate paths and optional ADR metadata updates. None of those
need to become part of the sorting loop.

`AzureDpsFramework` is now a thin adapter responsible for:

- Reading and validating our configuration.
- Loading a device key or bootstrap certificate and its chain.
- Generating/loading the CSR and preserving its matching private key.
- Saving the issued certificate chain durably.
- Opening a unified SDK session with those inputs.
- Keeping ADR ARM queries and metadata updates separate from the device protocol.

The SDK's `ConnectionClient` owns DPS registration, authentication, connection
retry and reprovisioning. `TelemetryClient` shares that connection to send the
colour readings. We no longer build DPS MQTT topics, poll registration responses,
or generate SAS tokens in our own C# transport.

## Framework Architecture

```text
Configuration + durable CSR/key/certificate files
                       |
                       v
             AzureDpsFramework adapter ----> ADR ARM metadata helpers
                       |
                       v
          Unified SDK ConnectionClient
             DPS provisioning + Hub connection
                       |
                       v
               SDK TelemetryClient
```

The application still calls its initialization and telemetry services. They
don't need to know how a DPS registration response is encoded on the wire.

## Keeping Both Attestation Methods

The two-phase certificate story from the earlier posts hasn't changed:
an onboarding credential proves who the device is to DPS; a separate CSR key
belongs to the operational certificate used with IoT Hub.

We also preserve the ordinary DPS profiles that don't request a new certificate:

| Onboarding | Request operational certificate? | IoT Hub credential |
|---|---|---|
| Symmetric key | No | Device-specific symmetric key |
| X.509 | No | Existing device certificate/key |
| Symmetric key + CSR | Yes | ADR-issued operational certificate |
| X.509 + CSR | Yes | ADR-issued operational certificate |

Set `RequestOperationalCertificate` explicitly. Its default remains `true` to
preserve the original CSR examples; use `false` for standard DPS. Prefer a
device-specific `DeviceKeyBase64` over handing every device a group master key.

## Certificate Handling Still Matters

Moving to an SDK doesn't make our filesystem disappear. Keep the CSR, matching
private key and issued chain across restarts:

- `CsrFilePath` is the CSR we submit.
- `CsrKeyFilePath` is the key that must match the returned certificate.
- `IssuedCertFilePath` holds the issued certificate chain.
- The `.connection.json` sidecar alongside the issued PEM keeps the assignment
  needed to restore the operational certificate and assigned Hub on restart.

Those paths can use `{RegistrationId}` and retain the existing
[configuration path rules](../docs/Configuration.md). Keep the files outside application releases and out of Git.
Retrying after a network interruption must not silently replace the key under a
certificate we've already saved.

`UnifiedDeviceSession` delegates all MQTT to the SDK; its coordinator holds the
updater lock only during DPS sessions, including reprovisioning, not for the
Hub connection lifetime. CSR profiles reject a missing issued chain before
Hub authentication rather than falling back to the onboarding credential.

## Retry and Renewal Aren't the Same Thing

`ConnectionClient` provides retry/reconnection and reprovisioning. The SDK also
offers optional IoT Hub certificate-renewal APIs, so renewal needn't always mean
going back through DPS.

That is SDK capability, not a claim that I've implemented automatic renewal in
this sorter. Scheduling renewal, persisting a replacement chain and recovering
from an interrupted replacement are still separate lifecycle work. There's no
new automatic seven-day renewal job hidden in this migration.

## What Hasn't Changed

We're still on .NET 10 and the same IoT Hub. The SDK discovers the assigned
endpoint/profile through DPS; we don't construct the hostname or force a
protocol. The SDK brings MQTTnet 5 into the sorter, but a library's major version
isn't a reason to migrate the Hub or rebuild our Azure resources.

The separate `AzureSoftwareUpdatesFramework` updater is also still separate.
It uses its own DPS **software-update** MQTT protocol and MQTTnet 4. The unified
.NET SDK doesn't implement that workflow, so removing custom provisioning code
doesn't remove the update client.

## Testing the New Boundary

From the repository root:

```powershell
# No network or physical hardware
dotnet run --project .\skittlesorter.csproj -- --self-test --config .\appsettings.json

# Real DPS + Hub, representative telemetry, graceful disconnect
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

The second command has a two-minute bound, waits for one telemetry acknowledgement
and doesn't initialize physical sensors or servos.
It does use real Azure credentials. For deliberate fresh-registration tests,
follow the [cloud-test runbook](../scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test):
the helper doesn't reset by default, and explicit `-ResetDevice` targets only
the named device/registration, never the enrollment, CA or resource group.

## What We Accomplished

- ✅ Removed the custom provisioning protocol instead of maintaining two versions
- ✅ Kept configuration and durable certificate handling behind a thin adapter
- ✅ Preserved standard DPS and both CSR onboarding flows
- ✅ Kept ADR metadata operations and software updates at their own boundaries
- ✅ Added a bounded way to check the cloud connection without the sorter hardware

## Next Steps

Now we'll put that smaller adapter into the **device application**: load the
configuration, open the unified connection and send the Skittle telemetry.

---

[Next: Building the Device Application →](06-building-device-application.md)
