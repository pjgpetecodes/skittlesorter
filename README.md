# Skittle Sorter

## What's this project about?

This project powers a 3D-printed Skittle sorter with Azure IoT integration. It combines hardware control (sensor + servos) with cloud provisioning and telemetry through Microsoft's unified .NET device SDK, including preview X.509 CSR issuance.

The hardware design is based on the [PTC Education Candy Sorter](https://github.com/PTC-Education/Candy-Sorter/) project.

## What's changed?

- The sorter now pins [`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4). Its unified `ConnectionClient` handles DPS provisioning and Hub connections; `TelemetryClient` sends telemetry. The custom C# DPS protocol and `Microsoft.Azure.Devices.Client` are removed.
- `AzureDpsFramework` remains a thin configuration, durable certificate/CSR persistence, ADR ARM, and unified-session adapter library. Standard symmetric-key and X.509 profiles and both CSR onboarding flows remain supported.
- .NET 10, the existing Hub, and cloud resource topology stay unchanged. The SDK discovers the assigned endpoint/profile through DPS; MQTTnet 5 is a sorter dependency, not an instruction to migrate the Hub.
- `--cloud-self-test --config PATH` performs a bounded provision/connect/telemetry/disconnect check without physical hardware. `--self-test` remains offline.
- The separate `AzureSoftwareUpdatesFramework` updater still uses its own DPS **software-update** MQTT protocol and MQTTnet 4. The unified SDK does not implement that workflow.

See Microsoft's [SDK design and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md) and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified). This is a preview migration, not a production-readiness claim. SDK retry/reprovision and optional IoT Hub certificate-renewal APIs are available; automatic operational-certificate renewal is not implemented here.

## Reuse the Azure Integration

For a new device application, start with the official unified SDK. Reuse this repository's `AzureDpsFramework` adapter when you also need its configuration, persisted CSR/key/issued-chain files, or ADR metadata helpers. Existing published framework versions with custom security providers are not the migrated API; see [AzureDpsFramework/README.md](AzureDpsFramework/README.md#nuget-distribution).

## Documentation
- Application updates: [two independent OTA demonstrations](docs/Application-Updates.md) and [GA blog series](blog/device-update-for-iot-hub/README.md), followed by [custom C# ADR Software Updates](blog/adr-software-updates/README.md). Local drafts; real Azure/Pi validation remains outstanding.
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

> ⚠️ **Preview features:** The unified package and IoT Hub/ADR certificate-management integration are in preview. Pin the package and review current service and SDK guidance before upgrading.

## Features

- **Color Detection**: Uses TCS3472x color sensor to identify Skittle colors (Red, Green, Yellow, Purple, Orange)
- **Automated Sorting**: Servo motors position and sort Skittles into separate chutes
- **Mock Mode**: Test without physical hardware using mock sensors and servos
- **Azure DPS with X.509 Certificates** *(Preview)*: Automatic device provisioning and certificate-based authentication using Azure Device Provisioning Service preview API (`2025-07-01-preview`) with CSR-based certificate issuance
  - Uses the official unified SDK; no custom C# DPS provisioning transport
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

For a bounded cloud check without sensors or servos:

```powershell
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

This has a two-minute bound, makes real Azure requests, waits for one representative telemetry acknowledgement, then disconnects gracefully. For exact-device reset and verification, see the [cloud test runbook](scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test); no reset is performed by default.

**Application flow:**
1. Provision device via DPS
2. Connect to IoT Hub with issued certificate
3. Begin sorting loop: pick → detect color → send telemetry → position chute → drop

Full quickstart and architecture details: **[docs/Quickstart.md](docs/Quickstart.md)** | **[docs/Architecture.md](docs/Architecture.md)**

## Architecture

Extended details: [docs/Architecture.md](docs/Architecture.md)

The sorter and its Azure adapter form the provisioning/telemetry path. The solution also contains independent update tooling and tests.

### AzureDpsFramework

A thin integration library for configuration, device-key derivation, certificate generation/persistence, ADR ARM metadata operations, and unified SDK sessions. **[📖 Full Documentation](AzureDpsFramework/README.md)**

The official `ConnectionClient` owns registration, authentication, MQTT transport, retry/reconnection and reprovisioning. `AzureDpsFramework` no longer defines its own security providers, provisioning transports, registration protocol, or SAS-token implementation. `UnifiedDeviceSession` keeps device-owned CSR/private-key files, issued PEM/chain and the `.connection.json` assignment sidecar durable across restarts. It rejects missing issuance in CSR profiles before Hub authentication and holds the shared updater lock only during DPS sessions, including reprovisioning, not for the Hub connection lifetime.

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
- **DpsInitializationService.cs**: Selects the configured profile and opens a unified SDK session through the adapter
- **TelemetryService.cs**: Sends detected Skittle colors through the SDK `TelemetryClient`

## Device Provisioning Flow

**Two attestation methods, with optional operational-certificate issuance:**

**Symmetric Key Flow:**
1. Derive device key from enrollment group key
2. Let the unified SDK authenticate to DPS
3. Submit a persisted CSR if `RequestOperationalCertificate` is `true`
4. Receive the assignment and, for CSR profiles, an issued X.509 chain
5. Connect to IoT Hub using the device key or issued certificate selected by the profile

**X.509 Certificate Flow:**
1. Load bootstrap certificate (device + intermediate + root chain)
2. Connect to DPS with X.509 client auth
3. DPS validates certificate chain (both root and intermediate must be verified)
4. If requested, submit a persisted CSR and receive a newly issued certificate
5. Connect to IoT Hub using the existing or newly issued certificate selected by the profile

**Key Point:** In CSR profiles, onboarding credentials authenticate to DPS and the issued operational certificate authenticates to IoT Hub. With `RequestOperationalCertificate: false`, standard symmetric-key and X.509 profiles connect without requesting a new certificate; symmetric-key mode continues using its device key. The compatibility default remains `true`.

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
