# Quickstart

A minimal path to build and run the sorter.

## What's changed?

.NET 10 is unchanged. The sorter pins Microsoft's unified
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** for DPS and telemetry, with a thin
`AzureDpsFramework` adapter. Use the existing Hub and current configuration;
the SDK discovers the assigned endpoint/profile through DPS. No manual Hub or
cloud-resource migration is required. See [provisioning profiles](DPS-Provisioning.md).

## Prerequisites
- .NET SDK (compatible with `net10.0`)
- Windows with PowerShell (or cross-platform shell)
- Azure subscription (required for IoT Hub, DPS, ADR)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- Azure IoT extension: `az extension add --name azure-iot`

## Setup

### 1. Azure Resources
Follow [Azure Setup](./Azure-Setup.md) to create:
- Resource Group, IoT Hub, DPS
- ADR namespace and credential policy
- Enrollment group (symmetric key or X.509)

### 2. X.509 Certificates (if using X.509 attestation)
Run the automated setup script:
```powershell
pwsh ./scripts/setup-x509-attestation.ps1 `
  -RegistrationId skittlesorter `
  -EnrollmentGroupId skittlesorter-group `
  -DpsName your-dps-name `
  -ResourceGroup your-rg `
  -CredentialPolicy cert-policy
```

This creates the full certificate hierarchy (root, intermediate, device) and configures DPS. Files are now generated per identity (for example, `skittlesorter-device.pem`) so multiple identities can coexist.

After it runs, ensure `appsettings.json` uses tokenized paths so switching identity only requires changing `RegistrationId`:
- `IoTHub:DpsProvisioning:RegistrationId` = the same value you passed to `-RegistrationId`
- `IoTHub:DeviceId` = `{RegistrationId}`
- `IoTHub:DpsProvisioning:AttestationCertPath` = `scripts/certs/device/{RegistrationId}-device.pem`
- `IoTHub:DpsProvisioning:AttestationKeyPath` = `scripts/certs/device/{RegistrationId}-device.key`
- `IoTHub:DpsProvisioning:AttestationCertChainPath` = `scripts/certs/ca/{RegistrationId}-chain.pem`

**Note:** For detailed explanation of the certificate hierarchy and verification process, see [Azure Setup](./Azure-Setup.md).

### 3. Configuration
Copy and configure the [safe template](../src/configuration/appsettings.template.json):
```powershell
cp src/configuration/appsettings.template.json appsettings.json
# Edit with your Azure values (IdScope, RegistrationId, etc.)
```

See [Configuration](./Configuration.md) for full details.

## Run
```powershell
# From repo root
# Build
dotnet build

# Run
dotnet run --project skittlesorter.csproj
```

## Bounded connectivity check

```powershell
# Offline JSON/configuration validation
dotnet run --project .\skittlesorter.csproj -- --self-test --config .\appsettings.json

# Real DPS + Hub connection, representative telemetry, then graceful disconnect
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

The cloud check never initializes physical hardware. It does contact Azure and
may create the configured device record. For repeat runs and explicit exact-device
reset, use the [unified cloud-test runbook](../scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test).
Neither check implements automatic certificate renewal.

## Next Steps
- **Hardware:** See [Hardware](./Hardware.md) for wiring and calibration
- **Testing again?** Prefer the bounded cloud check above. [Clean Test Start](./Clean-Test-Start.md) documents the guarded exact-device reset.
- **Troubleshooting:** See [Troubleshooting](./Troubleshooting.md) for common DPS and certificate issues
- **Telemetry:** See [Telemetry](./Telemetry.md) for monitoring IoT Hub messages
- **Security:** See [Security](./Security.md) for credential management best practices

## Related

- [Hardware](./Hardware.md)
- [Azure Setup](./Azure-Setup.md)
- [DPS Provisioning](./DPS-Provisioning.md)
- [Configuration](./Configuration.md)
- [Clean Test Start](./Clean-Test-Start.md)