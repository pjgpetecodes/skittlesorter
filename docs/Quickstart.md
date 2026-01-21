# Quickstart

A minimal path to build and run the sorter.

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

This creates the full certificate hierarchy (root, intermediate, device) and configures DPS.

**Note:** For detailed explanation of the certificate hierarchy and verification process, see [Azure Setup](./Azure-Setup.md).

### 3. Configuration
Copy and configure [appsettings.json](../appsettings.json):
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

## Next Steps
- **Hardware:** See [Hardware](./Hardware.md) for wiring and calibration
- **Testing again?** See [Clean Test Start](./Clean-Test-Start.md) to reset enrollment groups and devices for a fresh test
- **Troubleshooting:** See [Troubleshooting](./Troubleshooting.md) for common DPS and certificate issues
- **Telemetry:** See [Telemetry](./Telemetry.md) for monitoring IoT Hub messages
- **Security:** See [Security](./Security.md) for credential management best practices

## Related

- [Hardware](./Hardware.md)
- [Azure Setup](./Azure-Setup.md)
- [DPS Provisioning](./DPS-Provisioning.md)
- [Configuration](./Configuration.md)
- [Clean Test Start](./Clean-Test-Start.md)