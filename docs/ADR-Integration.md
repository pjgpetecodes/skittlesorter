# ADR Integration

Query and update devices in Azure Device Registry after provisioning.

## Capabilities
- List devices in a namespace
- Get device details (attributes, tags, system data)
- Update device properties (attributes, tags, enabled, OS version)

## Configuration-Driven Updates
- Controlled via `Adr.DeviceUpdate` section in `appsettings.json`.

## Related

- [Azure Setup](./Azure-Setup.md)
- [DPS Provisioning](./DPS-Provisioning.md)
- [Configuration](./Configuration.md)