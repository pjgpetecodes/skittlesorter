# Skittle Sorter

## What's this project about?

This project powers a 3D-printed Skittle sorter with Azure IoT integration. It combines hardware control (sensor + servos) with cloud provisioning via Azure DPS, including preview X.509 CSR issuance workflows that are not yet fully covered by the official C# SDK.

The hardware design is based on the [PTC Education Candy Sorter](https://github.com/PTC-Education/Candy-Sorter/) project.

## Use the NuGet Package (Fastest Path)

If you only need the DPS/ADR preview integration in your own C# app, use the `AzureDpsFramework` NuGet package instead of copying framework source files.

**Why use the package?**
- Keeps your app code clean (consume API, don’t fork internals)
- Lets you pin a known version and upgrade intentionally
- Gives teams a consistent integration path while official SDK support is still pending

**Quick start (consumer app):**
1. Add a GitHub Packages feed to your `NuGet.config` (owner feed URL):
  - `https://nuget.pkg.github.com/pjgpetecodes/index.json`
2. Install a pinned version:
  - `dotnet add package AzureDpsFramework --version 0.1.2-preview.1`
3. Restore/build:
  - `dotnet restore && dotnet build`

For full package feed setup and versioning details, see [AzureDpsFramework/README.md](AzureDpsFramework/README.md#nuget-distribution).

## Documentation
- Quickstart: [docs/Quickstart.md](docs/Quickstart.md)
- Hardware: [docs/Hardware.md](docs/Hardware.md)
- Azure Setup: [docs/Azure-Setup.md](docs/Azure-Setup.md)
- DPS Provisioning: [docs/DPS-Provisioning.md](docs/DPS-Provisioning.md)
- ADR Integration: [docs/ADR-Integration.md](docs/ADR-Integration.md)
- Configuration: [docs/Configuration.md](docs/Configuration.md)
- Architecture: [docs/Architecture.md](docs/Architecture.md)
- **[Clean Test Start](docs/Clean-Test-Start.md)** - Reset for fresh provisioning tests (script: scripts/clean-test-start.ps1)
- Troubleshooting: [docs/Troubleshooting.md](docs/Troubleshooting.md)
- Security: [docs/Security.md](docs/Security.md)
- Telemetry: [docs/Telemetry.md](docs/Telemetry.md)

> ⚠️ **Preview Features & Unsupported SDK**: This project uses Azure DPS preview API (`2025-07-01-preview`) with X.509 CSR-based certificate issuance. **There is currently no official C# SDK support for these preview features.** This implementation provides direct MQTT protocol integration as a workaround. See [Device Provisioning Flow](#device-provisioning-flow) for implementation details.

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

**Key Components:**
- TCS3472x color sensor (I2C)
- 2x Servo motors (GPIO)
- Raspberry Pi or compatible device

**Circuit diagram:** `circuit/Circuit.fzz`

Full wiring guide and pin mappings: **[docs/Hardware.md](docs/Hardware.md)**

## Azure Setup

See full guide: [docs/Azure-Setup.md](docs/Azure-Setup.md)

Complete Azure resource setup including:
- Resource Group, IoT Hub, and Device Provisioning Service (DPS)
- Azure Device Registry (ADR) namespace and credential policy
- Enrollment groups for symmetric key or X.509 certificate attestation
- Automated certificate hierarchy setup with proof-of-possession verification

## Configuration

See full reference: [docs/Configuration.md](docs/Configuration.md)

Create an `appsettings.json` file in the project root. Configuration includes:
- **Hardware**: Hardware-related settings grouped under one section
- **Hardware.MockMode**: Simulate hardware for development/testing
- **Hardware.ServoPositions**: Arm servo angles for pick/detect/drop operations
- **Hardware.ChutePositions**: Chute servo angles for each color chute
- **Hardware.ArmServoHardware**: PWM settings for arm servo (pick/detect/drop)
- **Hardware.ChuteServoHardware**: PWM settings for chute servo (sorting)
- **IoTHub**: DPS provisioning (symmetric key or X.509) and telemetry settings
- **Adr**: Azure Device Registry integration for device queries and updates

## Project Structure

```
skittlesorter/
├── src/                               # Application source code
│   ├── Program.cs                     # Entry point
│   ├── configuration/                 # Configuration management
│   │   ├── ConfigurationLoader.cs     # Loads appsettings.json
│   │   ├── appsettings.template.json  # Configuration template (no secrets)
│   │   ├── appsettings.sas.template.json
│   │   └── appsettings.x509.template.json
│   ├── drivers/                       # Hardware drivers
│   │   ├── TCS3472x.cs                # Color sensor driver
│   │   ├── SkittleSorterService.cs    # Main sorting logic
│   │   ├── ServoController.cs         # Servo motor control
│   │   ├── ServoHardwareConfig.cs     # Single-servo PWM config model
│   │   ├── ServoSystemHardwareConfig.cs # Dual-servo PWM config model
│   │   ├── MockColorSensorConfig.cs   # Mock sensor for testing
│   │   └── MockServoMotor.cs          # Mock servo for testing
│   └── comms/                         # Communication services
│       ├── DpsInitializationService.cs # DPS provisioning
│       └── TelemetryService.cs        # Telemetry to IoT Hub
├── appsettings.json                   # Configuration (root level, gitignored)
├── appsettings.sas.json
├── appsettings.x509.json
├── scripts/                           # Setup and automation scripts
│   ├── setup-x509-attestation.ps1     # Certificate hierarchy setup
│   └── X509_ATTESTATION_GUIDE.md      # X.509 guide
├── docs/                              # Complete documentation
├── AzureDpsFramework/                 # Reusable DPS library
├── certs/                             # Certificate directory (gitignored)
├── circuit/                           # Hardware circuit diagrams
└── resources/                         # Images and assets
```

**Configuration Files:**
- **Root level**: `appsettings.json`, `appsettings.sas.json`, `appsettings.x509.json` (actual configs, gitignored)
- **Templates**: `src/configuration/appsettings.*.template.json` (safe to commit, no secrets)

## Running the Application

```powershell
dotnet build
dotnet run --project skittlesorter.csproj
```

**Application flow:**
1. Provision device via DPS
2. Connect to IoT Hub with issued certificate
3. Begin sorting loop: pick → detect color → send telemetry → position chute → drop

Full quickstart and architecture details: **[docs/Quickstart.md](docs/Quickstart.md)** | **[docs/Architecture.md](docs/Architecture.md)**

## Architecture

Extended details: [docs/Architecture.md](docs/Architecture.md)

The solution is organized into two projects:

### AzureDpsFramework

A reusable class library implementing Azure Device Provisioning Service with X.509 certificate support. **[📖 Full Documentation](AzureDpsFramework/README.md)**

**Why a Custom MQTT Implementation?**

The official `Microsoft.Azure.Devices.Provisioning.Client` NuGet package does not yet support:
- The `2025-07-01-preview` DPS API version
- CSR-based X.509 provisioning (only supports pre-generated certificates or symmetric keys)
- The new Azure Device Registry (ADR) certificate issuance workflow

**Solution**: This library provides a **direct MQTT protocol implementation** (using MQTTnet) that communicates directly with the DPS MQTT endpoint, bypassing the SDK limitations. All DPS MQTT protocol specifications are implemented manually, enabling full support for preview features.

**Key Features**:
- ✅ Symmetric key authentication with device key derivation
- ✅ Automatic CSR generation (RSA or ECC)
- ✅ MQTT protocol implementation for DPS (port 8883 TLS)
- ✅ Certificate issuance via Azure Device Registry credential policies
- ✅ SAS token generation with proper URL encoding
- ✅ Polling support for async device assignment
- ✅ X.509 certificate loading with private key persistence

**Components**:

- **DpsConfiguration**: Loads and validates DPS settings from `appsettings.json`
- **DpsSasTokenGenerator**: Generates DPS-compatible SAS tokens with symmetric key derivation
- **CertificateManager**: Handles CSR generation, PEM persistence, and X.509 certificate loading
- **DpsProvisioningClient**: Orchestrates MQTT-based device registration flow

> **Implementation Note**: Since the official Microsoft.Azure.Devices.Provisioning.Client SDK does not yet support the preview DPS features (CSR-based X.509 provisioning via `2025-07-01-preview` API), this library provides a direct MQTT protocol implementation using MQTTnet. All communication follows the DPS MQTT protocol specification.
> 
> For detailed information about the authentication flow, MQTT protocol details, troubleshooting, and API reference, see **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)**.

### skittlesorter (Application)

Main application implementing the sorting logic, located in `src/`:

**Core files:**
- **src/Program.cs**: Entry point loading all configurations and running the application
- **src/configuration/ConfigurationLoader.cs**: Loads settings from `appsettings.json` (`Hardware`, IoT Hub, DPS, ADR) with backward-compatible fallback for legacy root-level hardware keys

**Hardware drivers (src/drivers/):**
- **TCS3472x.cs**: Color sensor driver
- **SkittleSorterService.cs**: Main sorting loop orchestration (color detection → servo movement → telemetry)
- **ServoController.cs**: Manages dual-servo positioning (arm + chute) with configuration-driven angle mappings
- **MockColorSensorConfig.cs**, **MockServoMotor.cs**: Mock implementations for testing without hardware

**Communication services (src/comms/):**
- **DpsInitializationService.cs**: Orchestrates DPS provisioning and returns authenticated `DeviceClient`
- **TelemetryService.cs**: Sends detected Skittle colors to IoT Hub

## Device Provisioning Flow

**Two attestation methods supported:**

**Symmetric Key Flow:**
1. Derive device key from enrollment group key
2. Generate SAS token for MQTT authentication
3. Connect to DPS and submit CSR
4. Receive newly issued X.509 certificate
5. Connect to IoT Hub using new certificate

**X.509 Certificate Flow:**
1. Load bootstrap certificate (device + intermediate + root chain)
2. Connect to DPS with X.509 client auth
3. DPS validates certificate chain (both root and intermediate must be verified)
4. Submit CSR and receive newly issued certificate
5. Connect to IoT Hub using new certificate

**Key Point:** Bootstrap credentials (symmetric key or X.509) are only for DPS authentication. All IoT Hub communication uses the newly issued certificate from the credential policy.

Full protocol details: **[docs/DPS-Provisioning.md](docs/DPS-Provisioning.md)** | **[docs/ADR-Integration.md](docs/ADR-Integration.md)** | **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)**

## Telemetry Format

**Each detected Skittle sends:**
- `messageId`: Sequential counter
- `deviceId`: Device identifier
- `color`: Detected color (Red, Green, Yellow, Purple, Orange)
- `timestamp`: ISO 8601 format
- Message property: `colorAlert=detected`

Full schema and monitoring guidance: **[docs/Telemetry.md](docs/Telemetry.md)**

## Development

**Mock Mode (for PC/laptop testing):**
- Set `Hardware.MockMode.EnableMockColorSensor` and `Hardware.MockMode.EnableMockServos` to `true`
- Customize `MockColorSequence` for different test patterns
- Full DPS provisioning and telemetry still work

**Hardware Mode (Raspberry Pi):**
- Set both mock settings to `false`
- Connect TCS3472x sensor via I2C
- Configure `Hardware.ArmServoHardware` and `Hardware.ChuteServoHardware` to match your PWM chip/channel or GPIO setup

**Cross-compile for Raspberry Pi:**
```bash
dotnet publish -c Release -r linux-arm64 --self-contained
```

Full development guide: **[docs/Quickstart.md](docs/Quickstart.md)**

Troubleshooting DPS and certificate issues: **[docs/Troubleshooting.md](docs/Troubleshooting.md)** | **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)**

## Security Best Practices

**Never commit:**
- `appsettings.json` (contains secrets)
- `certs/` directory (private keys)
- `*.pem`, `*.key`, `*.pfx` files

**Safe to commit:**
- `appsettings.template.json` (no secrets)
- Source code and documentation

**Best practices:**
- ✅ Use Azure Key Vault for production
- ✅ Restrict file permissions on certificate files (chmod 600)
- ✅ Rotate certificates before expiry
- ❌ Never hardcode secrets in code

Full security guidance: **[docs/Security.md](docs/Security.md)** | **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)**

## Security Considerations

**Key security points:**
- Store DPS enrollment keys securely (Key Vault for production)
- Protect `certs/` directory with restrictive permissions
- Rotate certificates before expiry
- Use TLS/SSL for all MQTT connections (port 8883)
- Private keys prove certificate ownership—keep them secure

Detailed security guidance: **[docs/Security.md](docs/Security.md)** | **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)**

## License

**Code**: MIT License (see LICENSE file)

**Hardware/3D Models**: Licensed under the same terms as the original [Candy Sorter repository](https://github.com/PTC-Education/Candy-Sorter/) (PTC Education)

You are free to use, modify, and distribute the code in this repository under the MIT License. The 3D printed sorter components are based on PTC Education's Candy Sorter design.
