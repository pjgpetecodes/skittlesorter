# Skittle Sorter

> ⚠️ **Preview Features & Unsupported SDK**: This project uses Azure DPS preview API (`2025-07-01-preview`) with X.509 CSR-based certificate issuance. **There is currently no official C# SDK support for these preview features.** This implementation provides direct MQTT protocol integration as a workaround. See [Device Provisioning Flow](#device-provisioning-flow) for implementation details.

This project drives a 3D printed Skittle Sorter with Azure IoT Hub integration using Azure Device Provisioning Service (DPS) with X.509 certificate-based authentication.

The hardware design is based on the [PTC Education Candy Sorter](https://github.com/PTC-Education/Candy-Sorter/) project.

## Features

- **Color Detection**: Uses TCS3472x color sensor to identify Skittle colors (Red, Green, Yellow, Purple, Orange)
- **Automated Sorting**: Servo motors position and sort Skittles into separate chutes
- **Mock Mode**: Test without physical hardware using mock sensors and servos
- **Azure DPS with X.509 Certificates** *(Preview)*: Automatic device provisioning and certificate-based authentication using Azure Device Provisioning Service preview API (`2025-07-01-preview`) with CSR-based certificate issuance
  - ⚠️ **Note**: Direct MQTT protocol implementation (C# SDK does not yet support these preview features)
- **IoT Hub Integration**: Sends detected Skittle colors with timestamps to Azure IoT Hub for real-time monitoring
- **Configuration-Driven**: All servo angles and chute positions externalized to `appsettings.json`

### Sorter in Action

![Skittle Sorter Animation](resources/animation.gif)

## Prerequisites

- .NET 10.0 SDK
- Azure Account with:
  - IoT Hub instance
  - Device Provisioning Service (DPS) instance with enrollment group
- Hardware:
  - Raspberry Pi or compatible device
  - TCS3472x color sensor
  - 2x Servo motors
  - 3D printed sorter components

## Hardware Wiring

### Circuit Diagram

![Circuit Diagram](circuit/circuit.png)

### Wiring Table

| Pi Pin | Item          | Pin |
|--------|---------------|-----|
| 1      | TCS34725      | LED |
| 2      | TCS34725      | VIN |
| 3      | TCS34725      | SDA |
| 5      | TCS34725      | SCL |
| 14     | Servo 1+2     | GND |
| 32     | Servo 1       | Pulse |
| 33     | Servo 2       | Pulse |

## Azure Setup

### 1. Create IoT Hub and Device Provisioning Service

1. **Create an IoT Hub** in the Azure Portal (standard tier or higher)
2. **Create a Device Provisioning Service** instance
3. **Link IoT Hub to DPS**:
   - In DPS, go to Linked IoT hubs
   - Add your IoT Hub
4. **Create an Enrollment Group**:
   - In DPS, go to Manage enrollments → Enrollment groups
   - Create a group with **Attestation Type: Symmetric Key**
   - Save the **Primary Key** (enrollment group key)
   - Note your **ID Scope**

### 2. Configure Device Credentials

Update `appsettings.json` with your DPS credentials (see Configuration section below).

## Configuration

Create an `appsettings.json` file in the project root with the following structure:

```json
{
  "MockMode": {
    "EnableMockColorSensor": true,
    "EnableMockServos": true,
    "MockColorSequence": [
      "Red",
      "Green",
      "Yellow",
      "Purple",
      "Orange"
    ]
  },
  "ServoPositions": {
    "PickAngle": 160,
    "DetectAngle": 60,
    "DropAngle": 0
  },
  "ChutePositions": {
    "Red": 22,
    "Green": 44,
    "Purple": 66,
    "Yellow": 88,
    "Orange": 112,
    "Default": 22
  },
  "IoTHub": {
    "DeviceConnectionString": "HostName=your-hub.azure-devices.net;DeviceId=your-device;SharedAccessKey=***",
    "DeviceId": "skittlesorter",
    "SendTelemetry": true,
    "DpsProvisioning": {
      "IdScope": "0ne01104302",
      "RegistrationId": "skittlesorter",
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "EnrollmentGroupKeyBase64": "your-enrollment-group-primary-key-base64-encoded",
      "DeviceKeyBase64": null,
      "CertificatePath": "certs/device.csr",
      "PrivateKeyPath": "certs/device.key",
      "IssuedCertificatePath": "certs/issued.pem",
      "ApiVersion": "2025-07-01-preview",
      "SasExpirySeconds": 3600,
      "AutoGenerateCsr": true,
      "MqttPort": 8883
    }
  }
}
```

### Configuration Options

#### MockMode

**When to use Mock Mode:**
- **On a regular PC/laptop**: Set both `EnableMockColorSensor` and `EnableMockServos` to `true`. Standard computers don't have GPIO pins or I2C interfaces required for physical sensors and servos.
- **On a Raspberry Pi with hardware**: Set both to `false` to use the actual TCS3472x color sensor and servo motors connected via GPIO.

Options:
- `EnableMockColorSensor`: Set to `true` to use simulated color readings
- `EnableMockServos`: Set to `true` to simulate servo movements
- `MockColorSequence`: Array of colors to cycle through in mock mode

#### ServoPositions

Configure servo motor angles for picker and sorter movements:
- `PickAngle`: Angle to pick up a Skittle (default: 160°)
- `DetectAngle`: Angle to position in color detection area (default: 60°)
- `DropAngle`: Angle to drop Skittle into chute (default: 0°)

#### ChutePositions

Configure servo angle for each color chute:
- `Red`, `Green`, `Purple`, `Yellow`, `Orange`: Servo angles for each color's chute
- `Default`: Fallback angle for unknown colors

#### IoTHub

**Standard Connection (without DPS):**
- `DeviceConnectionString`: Your Azure IoT Hub device connection string
- `DeviceId`: Your device identifier
- `SendTelemetry`: Enable/disable telemetry sending

**DPS Provisioning Configuration:**
- `IdScope`: Your DPS ID Scope
- `RegistrationId`: Device registration ID (e.g., `skittlesorter`)
- `ProvisioningHost`: DPS endpoint (usually `global.azure-devices-provisioning.net`)
- `EnrollmentGroupKeyBase64`: Base64-encoded enrollment group primary key
- `DeviceKeyBase64`: Pre-computed device key (optional; auto-derived if null)
- `CertificatePath`: Path to CSR file
- `PrivateKeyPath`: Path to private key file
- `IssuedCertificatePath`: Path to store the issued X.509 certificate
- `ApiVersion`: DPS API version (use `2025-07-01-preview` for CSR-based provisioning)
- `SasExpirySeconds`: SAS token TTL in seconds (default: 3600)
- `AutoGenerateCsr`: Auto-generate CSR and private key if files don't exist (default: true)
- `MqttPort`: MQTT port for DPS connection (default: 8883)

## Running the Application

```bash
dotnet restore
dotnet build
dotnet run
```

The application will:
1. Load configuration from `appsettings.json`
2. Initialize hardware (or mock mode)
3. Provision device with Azure DPS (auto-generates and issues X.509 certificate)
4. Connect to Azure IoT Hub using certificate authentication
5. Begin the sorting loop:
   - Pick up a Skittle
   - Read its color
   - Send telemetry to IoT Hub (detected colors only)
   - Position the chute based on color
   - Drop the Skittle
   - Repeat

## Architecture

The solution is organized into two projects:

### AzureDpsFramework

A reusable class library implementing Azure Device Provisioning Service with X.509 certificate support.

**Why a Custom MQTT Implementation?**

The official `Microsoft.Azure.Devices.Provisioning.Client` NuGet package does not yet support:
- The `2025-07-01-preview` DPS API version
- CSR-based X.509 provisioning (only supports pre-generated certificates or symmetric keys)
- The new certificate issuance workflow

**Solution**: This library provides a **direct MQTT protocol implementation** (using MQTTnet) that communicates directly with the DPS MQTT endpoint, bypassing the SDK limitations. All DPS MQTT protocol specifications are implemented manually, enabling full support for preview features.

**Components**:

- **DpsConfiguration**: Loads and validates DPS settings from `appsettings.json`
- **DpsSasTokenGenerator**: Generates DPS-compatible SAS tokens with symmetric key derivation
- **CertificateManager**: Handles CSR generation, PEM persistence, and X.509 certificate loading
- **DpsProvisioningClient**: Orchestrates MQTT-based device registration flow

> **Implementation Note**: Since the official Microsoft.Azure.Devices.Provisioning.Client SDK does not yet support the preview DPS features (CSR-based X.509 provisioning via `2025-07-01-preview` API), this library provides a direct MQTT protocol implementation using MQTTnet. All communication follows the DPS MQTT protocol specification.

### skittlesorter (Application)

Main application implementing the sorting logic:

- **ConfigurationLoader**: Loads all configuration from `appsettings.json` (mock mode, IoT Hub, DPS, servo positions, chute positions)
- **ServoController**: Manages servo positioning with configuration-driven angle mappings
- **TelemetryService**: Sends detected Skittle colors to IoT Hub
- **DpsInitializationService**: Orchestrates DPS provisioning and returns authenticated `DeviceClient`
- **SkittleSorterService**: Main sorting loop orchestration (color detection → servo movement → telemetry)
- **Program.cs**: Entry point loading all configurations and running the application

## Device Provisioning Flow

When the application starts with DPS enabled:

1. **Load DPS Configuration**: Read from `appsettings.json`
2. **Generate CSR (if needed)**: Auto-create device certificate signing request and private key
3. **Derive Device Key**: Use HMACSHA256(base64Decode(enrollmentGroupKey), lowercase(registrationId))
4. **Generate SAS Token**: Create signed MQTT credentials with DPS endpoint
5. **MQTT Registration**: Connect to DPS, publish CSR, and request provisioning
6. **Poll for Status**: Wait for DPS to assign device to IoT Hub and issue certificate
7. **Load Certificate**: Parse PEM certificate and private key into X509Certificate2
8. **Connect to IoT Hub**: Authenticate using certificate-based connection
9. **Begin Sorting**: Start main application loop

## Telemetry Format

Each detected Skittle sends a message to IoT Hub with the following structure:

```json
{
  "messageId": 1,
  "deviceId": "skittlesorter",
  "color": "Red",
  "timestamp": "2026-01-17T10:30:45.123Z",
  "detectionTime": "2026-01-17 10:30:45.123"
}
```

Message properties include:
- `colorAlert`: Set to "detected" for all valid Skittle colors

## Development

### Testing Without Hardware (Mock Mode)

Perfect for development and testing on a regular PC where physical hardware isn't available:

1. Set both `EnableMockColorSensor` and `EnableMockServos` to `true` in `appsettings.json`
2. Customize the `MockColorSequence` to test different color patterns
3. The application will simulate the full sorting process, cycling through the specified colors
4. All IoT Hub telemetry will still be sent (if enabled), allowing you to test the cloud integration and DPS provisioning

### Running on Raspberry Pi with Hardware

When running on a Raspberry Pi with the physical sorter assembled:

1. Set both `EnableMockColorSensor` and `EnableMockServos` to `false`
2. Ensure your TCS3472x sensor is connected via I2C
3. Ensure your servo motors are connected to the appropriate GPIO pins
4. The application will use real hardware for color detection and sorting
5. Device provisioning and certificate authentication will work seamlessly with DPS

### Customizing Servo and Chute Angles

All servo angles are externalized in `appsettings.json`. No recompilation needed to adjust positions:

- Modify `ServoPositions` to change pick/detect/drop angles
- Modify `ChutePositions` to change angle mappings for each color
- Restart the application to apply changes

### Building for Raspberry Pi from Windows

```bash
# Cross-compile to Linux ARM64 (Raspberry Pi 4/5)
dotnet publish -c Release -r linux-arm64 --self-contained
```

Then transfer the published output to your Raspberry Pi and run.

## Security Considerations

- **DPS Credentials**: Store your enrollment group key securely. Never commit `appsettings.json` with real credentials to version control.
- **Certificates**: Device certificates are auto-generated and stored locally. Protect the `certs/` directory.
- **Connection Strings**: If using direct IoT Hub connection (without DPS), keep connection strings in environment variables or Azure Key Vault, never in code.
- **MQTT Port 8883**: Requires TLS/SSL. DPS endpoint is `global.azure-devices-provisioning.net:8883`.

## License

**Code**: MIT License (see LICENSE file)

**Hardware/3D Models**: Licensed under the same terms as the original [Candy Sorter repository](https://github.com/PTC-Education/Candy-Sorter/) (PTC Education)

You are free to use, modify, and distribute the code in this repository under the MIT License. The 3D printed sorter components are based on PTC Education's Candy Sorter design.
