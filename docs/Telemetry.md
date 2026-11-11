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

### Stream telemetry with Azure CLI

Use a terminal signed into the subscription containing the Hub. If necessary,
sign in, select the subscription, and install the IoT extension first:

```powershell
az login --use-device-code
az account set --subscription "<subscription-id>"
az extension add --name azure-iot
```

For the Pi demo identity:

```powershell
az iot hub monitor-events --hub-name pjgiothubdemo002 --device-id skittlesorter2
```

The same command works in Bash on the Pi or in Azure Cloud Shell. Leave it
running while the sorter sends telemetry; press **Ctrl+C** to stop. This reads
live message contents, unlike portal metrics, which show aggregate counts.

Use `--device-id skittlesorter` to monitor the PC identity, or omit
`--device-id` to monitor all devices on the Hub. For another deployment, use the
Hub assigned by DPS rather than an old Hub name from a connection string.

The monitoring account needs access to read the Hub's built-in event endpoint.
Device X.509 authentication does not grant CLI monitoring access. If the
command reports `Unauthorized`, check the operator's permissions and selected
subscription rather than changing the device's certificate or DPS scope.

## Related

- [Architecture](./Architecture.md)
- [Configuration](./Configuration.md)