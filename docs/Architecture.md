# Architecture

Components and flow of the sorter.

## What's changed?

The sorter uses `Microsoft.Azure.Iot.Device` **2.0.0-preview4** on .NET 10.
One unified `ConnectionClient` owns DPS provisioning and the IoT Hub connection;
`TelemetryClient` shares that connection. SDK retry/reconnection and reprovisioning
replace the custom C# provisioning protocol and legacy `DeviceClient`.

The existing Hub and Azure resource topology are unchanged. The SDK discovers
the assigned endpoint/profile through DPS; don't reconstruct the hostname or
force a protocol. MQTTnet 5 is a dependency upgrade, not a Hub migration. See the official
[SDK design and capability matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md).

## Components

**Application Core:**
- [src/Program.cs](../src/Program.cs) - Entry point and main initialization
- [src/configuration/ConfigurationLoader.cs](../src/configuration/ConfigurationLoader.cs) - Configuration management

**Hardware Drivers (src/drivers/):**
- [TCS3472x.cs](../src/drivers/TCS3472x.cs) - Color sensor driver
- [ServoController.cs](../src/drivers/ServoController.cs) - Servo motor control
- [SkittleSorterService.cs](../src/drivers/SkittleSorterService.cs) - Main sorting logic
- [MockColorSensorConfig.cs](../src/drivers/MockColorSensorConfig.cs) - Mock sensor for testing
- [MockServoMotor.cs](../src/drivers/MockServoMotor.cs) - Mock servo for testing

**Communication (src/comms/):**
- [DpsInitializationService.cs](../src/comms/DpsInitializationService.cs) - DPS provisioning
- [TelemetryService.cs](../src/comms/TelemetryService.cs) - Azure IoT Hub telemetry

**Azure Integration:**
- [AzureDpsFramework/](../AzureDpsFramework/) - Thin configuration, certificate/CSR persistence, ADR ARM, and unified-session adapter; no custom DPS security providers, transports, registration, or SAS-token protocol

## Flow
- Initialize
- Provision device (DPS)
- Optional ADR update and details fetch
- Operate sorting and telemetry

Standard symmetric-key and X.509 profiles skip CSR issuance when
`RequestOperationalCertificate` is `false`. Its compatibility default is `true`,
preserving symmetric-key-to-CSR and X.509-to-CSR onboarding. CSR/key files and
issued chains remain durable, outside application releases. SDK IoT Hub renewal
APIs are available, but automatic certificate renewal is not implemented.

`--self-test --config PATH` validates configuration offline.
`--cloud-self-test --config PATH` bypasses physical hardware and performs a
two-minute-bounded provisioning, connection, one acknowledged representative
telemetry message and graceful disconnect cycle.

`UnifiedDeviceSession` delegates MQTT to the SDK. Its coordinator holds the
updater lock only while DPS sessions are active, including reprovisioning, not
throughout the Hub connection lifetime. Issued PEM/chain, CSR/key and the
`.connection.json` assignment sidecar remain durable so the operational
certificate and assigned Hub can be restored. CSR profiles reject a missing
issued chain before authenticating to the Hub.

## Optional application-update boundary

The actual sorting loop in `Program.cs` supports safe cycle-boundary shutdown,
external configuration and atomic version/process readiness. Feature builds count
completed drops and add version/session-count telemetry. The baseline build keeps
legacy pre-drop telemetry timing.

GA Device Update uses Microsoft's Linux agent as a module identity under the sorter
device. ADR Software Updates uses an isolated custom C# DPS **software-update**
MQTT client, not a second Hub connection or the removed provisioning transport.
The unified .NET SDK does not implement that update workflow; the separate updater
retains MQTTnet 4. These paths require separate Hubs and only one controller
may install on the Pi at a time. Both delegate application replacement to the same
transactional local installer, with configuration and credentials outside releases.
`Adr.DeviceUpdate` metadata is not the executable update client.

See [application updates](Application-Updates.md) for the draft runbook and live
validation gates. Neither OS updates nor future official .NET update APIs are
implemented by this boundary.

## Related

- [DPS Provisioning](./DPS-Provisioning.md)
- [ADR Integration](./ADR-Integration.md)
- [Configuration](./Configuration.md)