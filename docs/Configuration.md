# Configuration Reference

Complete configuration guide for hardware, Azure IoT Hub, DPS, and ADR settings.

## What's changed?

Provisioning and telemetry use the unified `Microsoft.Azure.Iot.Device`
**2.0.0-preview4** package. Existing profile selection and durable CSR/key/issued
chain paths are retained; the adapter translates configuration into SDK options
rather than building DPS MQTT topics or SAS tokens.

## Configuration File

Service deployments use `--config /etc/skittlesorter/appsettings.json`; the default
repository invocation still reads `appsettings.json`. Published releases deliberately
exclude the real configuration. `--self-test --config PATH` checks its JSON structure
without moving hardware or making network requests; `--version` prints the running
assembly version. `--ready-file PATH` enables service readiness and requires an
explicit `Hardware` section.

`--cloud-self-test --config PATH` is a separate, bounded **online** check. It
has a two-minute bound, provisions/connects, waits for one representative telemetry
acknowledgement and disconnects without
initializing physical sensors or servos. It needs real credentials and network
access; it does not replace the offline `--self-test`.

For GA Device Update, start from
`src/configuration/appsettings.device-update.template.json`. Set
`IoTHub.DpsProvisioning.RequestOperationalCertificate` to `false` for standard DPS
without CSR issuance; it defaults to `true` for compatibility with existing ADR
profiles. Standard symmetric-key mode accepts `DeviceKeyBase64` directly. Do not
distribute a group master key when an individual enrollment key is available.
`ProvisioningLockPath` optionally coordinates app provisioning with the separate ADR
updater; both must use the same path. Relative certificate paths retain the
repository compatibility rule: when the selected config is below a project
directory, they resolve from that project root; otherwise they resolve from the
config's directory. Absolute paths are unchanged. Keep all keys and state outside
application releases.

Retain the `.connection.json` assignment sidecar alongside the issued PEM as well
as the CSR/key and full issued chain. The adapter uses it to restore the operational
certificate and assigned Hub on restart. CSR profiles reject a missing issued
chain before Hub authentication; they must not silently use the onboarding
credential instead.

`Adr.DeviceUpdate` remains registry metadata, not OTA configuration. The optional
custom updater has its own configuration; see [application updates](Application-Updates.md).

Create an `appsettings.json` file at the project root. You can start from the template:

```bash
cp src/configuration/appsettings.template.json appsettings.json
```

See [appsettings.template.json](../src/configuration/appsettings.template.json) for the complete template.

Basic structure:

```json
{
  "MockMode": { /* mock sensors/servos */ },
  "ServoPositions": { /* pick/detect/drop angles */ },
  "ChutePositions": { /* angles per color */ },
  "IoTHub": { "DpsProvisioning": { /* DPS settings */ } },
  "Adr": { "DeviceUpdate": { /* attributes/tags */ } }
}
```

## Configuration Sections

### MockMode

**When to use Mock Mode:**
- **On a regular PC/laptop**: Set both `EnableMockColorSensor` and `EnableMockServos` to `true`. Standard computers don't have GPIO pins or I2C interfaces required for physical sensors and servos.
- **On a Raspberry Pi with hardware**: Set both to `false` to use the actual TCS3472x color sensor and servo motors connected via GPIO.

**Options:**
- `EnableMockColorSensor`: Set to `true` to use simulated color readings
- `EnableMockServos`: Set to `true` to simulate servo movements
- `MockColorSequence`: Array of colors to cycle through in mock mode

### ServoPositions

Configure servo motor angles for picker and sorter movements:
- `PickAngle`: Angle to pick up a Skittle (default: 160°)
- `DetectAngle`: Angle to position in color detection area (default: 60°)
- `DropAngle`: Angle to drop Skittle into chute (default: 0°)

### ChutePositions

Configure servo angle for each color chute:
- `Red`, `Green`, `Purple`, `Yellow`, `Orange`: Servo angles for each color's chute
- `Default`: Fallback angle for unknown colors

### IoTHub

#### Standard Connection (without DPS)
- `DeviceConnectionString`: Your Azure IoT Hub device connection string
- `DeviceId`: Your device identifier (supports `{RegistrationId}` token)
- `SendTelemetry`: Enable/disable telemetry sending

#### DPS Provisioning Configuration
- `IdScope`: Your DPS ID Scope
- `RegistrationId`: Device registration ID (e.g., `skittlesorter`)
- `{RegistrationId}` token support: You can use this token in path fields (and `IoTHub.DeviceId`) to switch identities by changing only `RegistrationId`
- `AttestationMethod`: `"SymmetricKey"` (default) or `"X509"` for certificate-based authentication
- `ProvisioningHost`: DPS endpoint (usually `global.azure-devices-provisioning.net`)

#### For Symmetric Key Attestation
- `EnrollmentGroupKeyBase64`: Base64-encoded enrollment group primary key
- `DeviceKeyBase64`: Pre-computed device key (optional; auto-derived if null)
- Leave `AttestationCertPath`, `AttestationKeyPath`, `AttestationCertChainPath` empty

#### For X.509 Certificate Attestation
- `AttestationCertPath`: Path to device certificate (e.g., `scripts/certs/device/{RegistrationId}-device.pem`)
- `AttestationKeyPath`: Path to device private key (e.g., `scripts/certs/device/{RegistrationId}-device.key`)
- `AttestationCertChainPath`: Path to certificate chain file (e.g., `scripts/certs/ca/{RegistrationId}-chain.pem` containing intermediate + root)
- Leave `EnrollmentGroupKeyBase64` empty

#### Common Settings
- `CsrFilePath`: Path to CSR file for new certificate issuance (supports `{RegistrationId}`)
- `CsrKeyFilePath`: Path to private key for CSR (supports `{RegistrationId}`)
- `IssuedCertFilePath`: Path to store the issued X.509 certificate (supports `{RegistrationId}`)
- `ApiVersion`: Legacy configuration field; the unified SDK selects its supported protocol/API version, so this no longer overrides the wire protocol
- `SasExpirySeconds`: DPS SAS-token lifetime passed to the SDK for symmetric-key authentication (default: 3600); token generation remains SDK-owned
- `AutoGenerateCsr`: Auto-generate CSR and private key if files don't exist (default: true)
- `MqttPort`: MQTT TLS port; this adapter supports only 8883 and rejects other values
- `EnableDebugLogging`: Enable SDK/MQTT diagnostics for troubleshooting (default: false); review logs for sensitive identifiers and payloads before sharing

#### Debugging DPS Connection Issues
Set `EnableDebugLogging: true` for additional SDK diagnostics. The old custom
transport's topic/polling log format no longer applies. Microsoft's SDK uses
`System.Diagnostics.Trace`; see its [logging guidance](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md#capturing-logs).
Logs may contain hostnames, identities, topics or message contents. Restrict access
and redact them before sharing.

Leave it `false` for cleaner production output.

### Adr (Azure Device Registry)

Configuration for querying and updating device information in Azure Device Registry post-provisioning.

#### Prerequisites
- Azure Device Registry namespace created in same resource group/subscription
- Your app's identity has `Reader` role on the ADR namespace (for list/get operations)
- Metadata updates additionally require permission to write registry devices
- Microsoft.DeviceRegistry provider registered for your subscription

#### Options
- `Enabled`: Enable/disable ADR device listing and updates (default: false)
- `SubscriptionId`: Azure subscription ID containing the ADR namespace
- `ResourceGroupName`: Azure resource group containing the ADR namespace
- `NamespaceName`: ADR namespace name

#### DeviceUpdate (Post-provisioning Updates)
- `Enabled`: Automatically update device after provisioning (default: false)
- `Attributes`: Compatibility configuration container accepting only exact lower-camel-case `manufacturer`, `model`, `hardwareRevision`, and `softwareRevision`; these map to registry-device properties, not an arbitrary ARM `attributes` object
- `Tags`: String key-value resource tags; use these for custom labels such as device type, owner, hardware ID, environment or location
- `DeviceEnabled`: Maps to ARM `enablementState: "Enabled"` or `"Disabled"` (default: true)
- `OperatingSystemVersion`: Compatibility name mapped to ARM `softwareRevision` (optional); when supplied, it takes precedence over `Attributes.softwareRevision`

The current `namespaces/registryDevices` API (`2026-11-02-preview`) does not
accept arbitrary attributes or an `operatingSystemVersion` property. Unsupported
`Attributes` keys are rejected explicitly before the update request. Move custom
labels to `Tags` rather than renaming them to an unsupported property.

#### Example ADR Configuration

```json
"Adr": {
  "Enabled": true,
  "SubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "ResourceGroupName": "your-rg",
  "NamespaceName": "your-adr-namespace",
  "DeviceUpdate": {
    "Enabled": true,
    "Attributes": {
      "manufacturer": "Raspberry Pi Foundation",
      "model": "Raspberry Pi 4B",
      "hardwareRevision": "1.4"
    },
    "Tags": {
      "environment": "demo",
      "line": "A1",
      "deviceType": "machinery",
      "deviceOwner": "Operations",
      "hardwareId": "RPI4B-12345678"
    },
    "DeviceEnabled": true,
    "OperatingSystemVersion": "1.0.0"
  }
}
```

#### ADR Workflow

When enabled, after DPS provisioning completes:
1. **List Devices**: Query ADR namespace to list all devices (max 5 shown in logs)
2. **Update Device**: Optionally PATCH supported registry-device properties and tags, mapping the legacy enabled/OS configuration names as described above
3. **Fetch Device**: Retrieve updated device details including location, etag, tags, and systemData
4. **Log Results**: Print device details to console with color-coded ADR section headers

## Security Best Practices

- Keep secrets out of source control
- Never commit `appsettings.json` with real credentials
- Use Azure Key Vault for production secrets
- Use environment-specific configuration overrides when needed
- Protect certificate private keys with appropriate file permissions

## Related

- [Quickstart](./Quickstart.md)
- [Azure Setup](./Azure-Setup.md)
- [ADR Integration](./ADR-Integration.md)