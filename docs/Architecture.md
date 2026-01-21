# Architecture

Components and flow of the sorter.

## Components
- Application (`Program.cs`)
- Hardware abstractions (`TCS3472x.cs`, `MockServoMotor.cs`)
- Azure integration (DPS, ADR via REST)

## Flow
- Initialize
- Provision device (DPS)
- Optional ADR update and details fetch
- Operate sorting and telemetry

## Related

- [DPS Provisioning](./DPS-Provisioning.md)
- [ADR Integration](./ADR-Integration.md)
- [Configuration](./Configuration.md)