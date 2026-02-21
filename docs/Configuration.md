# Configuration Reference

Complete configuration guide for hardware, Azure IoT Hub, DPS, and ADR settings.

## Configuration File

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
- `ApiVersion`: DPS API version (use `2025-07-01-preview` for CSR-based provisioning)
- `SasExpirySeconds`: SAS token TTL in seconds (default: 3600) - only used for symmetric key attestation
- `AutoGenerateCsr`: Auto-generate CSR and private key if files don't exist (default: true)
- `MqttPort`: MQTT port for DPS connection (default: 8883)
- `EnableDebugLogging`: Enable verbose MQTT protocol logging for troubleshooting (default: false)

#### Debugging DPS Connection Issues
Set `EnableDebugLogging: true` to see detailed MQTT protocol messages including:
- Connection parameters and credentials (lengths only, not actual keys)
- Message topics and payload sizes
- Response parsing and polling attempts
- Status updates during provisioning

Leave it `false` for cleaner production output.

### Adr (Azure Device Registry)

Configuration for querying and updating device information in Azure Device Registry post-provisioning.

#### Prerequisites
- Azure Device Registry namespace created in same resource group/subscription
- Your app's identity has `Reader` role on the ADR namespace (for list/get operations)
- Microsoft.DeviceRegistry provider registered for your subscription

#### Options
- `Enabled`: Enable/disable ADR device listing and updates (default: false)
- `SubscriptionId`: Azure subscription ID containing the ADR namespace
- `ResourceGroupName`: Azure resource group containing the ADR namespace
- `NamespaceName`: ADR namespace name

#### DeviceUpdate (Post-provisioning Updates)
- `Enabled`: Automatically update device after provisioning (default: false)
- `Attributes`: Device attributes object (e.g., deviceType, deviceOwner, deviceCategory)
- `Tags`: Device tags (key-value pairs for resource tagging)
- `DeviceEnabled`: Enable/disable the device (default: true)
- `OperatingSystemVersion`: Device OS version string (optional)

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
      "deviceType": "machinery",
      "deviceOwner": "Operations",
      "deviceCategory": 16
    },
    "Tags": {
      "environment": "demo",
      "line": "A1"
    },
    "DeviceEnabled": true,
    "OperatingSystemVersion": "1.0.0"
  }
}
```

#### ADR Workflow

When enabled, after DPS provisioning completes:
1. **List Devices**: Query ADR namespace to list all devices (max 5 shown in logs)
2. **Update Device**: Optionally apply configured attributes, tags, enabled state, and OS version via REST PATCH
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