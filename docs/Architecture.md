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

## Related

- [DPS Provisioning](./DPS-Provisioning.md)
- [ADR Integration](./ADR-Integration.md)
- [Configuration](./Configuration.md)