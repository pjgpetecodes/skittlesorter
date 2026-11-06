# Telemetry & Monitoring

Payloads and monitoring guidance.

## What's changed?

Telemetry now uses `TelemetryClient` from `Microsoft.Azure.Iot.Device`
**2.0.0-preview4**, sharing the provisioning/Hub `ConnectionClient`. The legacy
`Microsoft.Azure.Devices.Client.DeviceClient` is no longer used. Existing JSON
payloads and message properties remain the application contract; this does not
change the Hub, routes, or resource topology; DPS discovery supplies the SDK's
assigned connection profile.

`--cloud-self-test --config PATH` sends representative telemetry and disconnects
gracefully without physical hardware. It is an online connectivity check, not a
physical sorting test.

## Payloads
- Color detection events
- Sort outcomes and counts

## Monitoring
- IoT Hub message routing
- Alerts and dashboards

## Related

- [Architecture](./Architecture.md)
- [Configuration](./Configuration.md)