# ADR Integration

Query and update devices in Azure Device Registry after provisioning.

## Capabilities
- List devices in a namespace
- Get registry-device properties, tags and system data
- Update `manufacturer`, `model`, `hardwareRevision`, `softwareRevision` and `enablementState`, plus resource tags

## Configuration-Driven Updates
- Controlled via `Adr.DeviceUpdate` section in `appsettings.json`.
- The current ARM resource is `namespaces/registryDevices`, API `2026-11-02-preview`.
- `Attributes` is a compatibility configuration container: only exact keys
  `manufacturer`, `model`, `hardwareRevision` and `softwareRevision` are accepted
  and mapped to ARM properties. Arbitrary keys are rejected explicitly.
- `DeviceEnabled` maps to `enablementState: "Enabled"`/`"Disabled"`;
  `OperatingSystemVersion` maps to `softwareRevision` and takes precedence over
  `Attributes.softwareRevision` when both are supplied.
- Use string-valued `Tags` for custom labels such as hardware ID, owner, location
  or device category, not unsupported attributes or an ARM `operatingSystemVersion`.

## Related

- [Azure Setup](./Azure-Setup.md)
- [DPS Provisioning](./DPS-Provisioning.md)
- [Configuration](./Configuration.md)