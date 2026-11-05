# Architecture

Components and flow of the sorter.

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
- [AzureDpsFramework/](../AzureDpsFramework/) - Reusable DPS library with MQTT protocol implementation

## Flow
- Initialize
- Provision device (DPS)
- Optional ADR update and details fetch
- Operate sorting and telemetry

## Optional application-update boundary

The actual sorting loop in `Program.cs` supports safe cycle-boundary shutdown,
external configuration and atomic version/process readiness. Feature builds count
completed drops and add version/session-count telemetry. The baseline build keeps
legacy pre-drop telemetry timing.

GA Device Update uses Microsoft's Linux agent as a module identity under the sorter
device. ADR Software Updates uses an isolated custom C# DPS update client, not a
second Hub connection. These paths require separate Hubs and only one controller
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