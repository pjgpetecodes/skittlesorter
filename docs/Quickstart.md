# Quickstart

A minimal path to build and run the sorter.

## What's changed?

.NET 10 is unchanged. The sorter pins Microsoft's unified
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** for DPS and telemetry, with a thin
`AzureDpsFramework` helpers. Use the existing Hub and current configuration;
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
Copy and configure the [safe default template](../appsettings.template.json):
```powershell
Copy-Item .\appsettings.template.json .\appsettings.json
# Edit with your Azure values (IdScope, RegistrationId, etc.)
```

The root `appsettings.json` is the default certificate-based X.509 profile.
Plain `dotnet run` loads it automatically. For an alternate symmetric-key or
X.509 profile, use the matching `appsettings.sas.template.json` or
`appsettings.x509.template.json` template; see [Configuration](./Configuration.md).

See [Configuration](./Configuration.md) for full details.

## Run
```powershell
# From repo root
# Build
dotnet build

# Run using the root appsettings.json
dotnet run
```

## Bounded connectivity check

```powershell
# Offline JSON/configuration validation
dotnet run -- --self-test

# Real DPS + Hub connection, representative telemetry, then graceful disconnect
dotnet run -- --cloud-self-test
```

The cloud check never initializes physical hardware. It does contact Azure and
may create the configured device record. For repeat runs and explicit exact-device
reset, use the [unified cloud-test runbook](../scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test).
Neither check implements automatic certificate renewal.

## Raspberry Pi: update and provision

Use `main` on both the development PC and Pi. The fixes from
`fix-broken-servo-control`, including physical PWM and chute control, are already
merged into `main`. An older branch can still run the legacy DPS/certificate
code even when its configuration has been updated.

Stop the sorter, then check the Pi's checkout:

```bash
git branch --show-current
git rev-parse --short HEAD
git status --short
```

Preserve local changes before switching branches. In particular, older checkouts
may have these three untracked templates, which are tracked on `main`. If they
are shown as untracked, move them aside rather than deleting them:

```bash
mkdir -p pi-update-backup
mv -t pi-update-backup appsettings.sas.template.json appsettings.template.json appsettings.x509.template.json
```

Then update and rebuild:

```bash
git switch main
git pull --ff-only
dotnet clean
dotnet run -- --self-test
dotnet run -- --cloud-self-test
```

Do not force the switch or discard tracked hardware/image changes if Git
reports a conflict. Compare the resulting revision with the development PC.
Keep the ignored `appsettings.json` and private certificate files; Git does not
transfer those files to the Pi.

Use separate identities, for example `skittlesorter` on the mock-hardware PC
and `skittlesorter2` on the physical Pi. On the Pi, keep
`EnableMockColorSensor` and `EnableMockServos` set to `false`. Set
`RegistrationId` to the Pi certificate's subject CN and obtain `IdScope` from
the actual DPS service, not from an older resource group's profile:

```bash
az iot dps show --name "<dps-name>" --resource-group "<resource-group>" --query properties.idScope --output tsv
```

If bootstrap certificates were regenerated for a different trusted CA, securely
copy the new device certificate, its private key, and public issuer chain to
the configured Pi locations. For example, the `-002` bootstrap filenames are:

- `scripts\certs\device\skittlesorter2-device-002.pem`
- `scripts\certs\device\skittlesorter2-device-002.key`
- `scripts\certs\ca\skittlesorter2-chain-002.pem`

`-002` identifies that certificate set; it does not change the registration ID.
Do not rename old certificates to these names: their issuer must match the CA
trusted by the selected DPS enrollment. Never copy CA private keys to the Pi.
Protect the device key so only the account running the sorter can read it.
Retain the separate operational CSR/key, issued chain, and assignment sidecar
across restarts.

For this X.509/CSR flow, use `RequestOperationalCertificate: true` (also the
default) and leave the unused `IoTHub.DeviceConnectionString` empty. Do not
retain an old Hub's connection string in the profile or paste its secret into
logs/issues; rotate any exposed device key.

The cloud self-test verifies DPS, Hub connection, and telemetry without moving
the hardware. After it passes, use `dotnet run` for normal sorting. Azure CLI
is not required for device X.509 authentication; it is needed only if you choose
CLI-based access for the app's optional [ADR management calls](ADR-Integration.md#authentication-on-a-raspberry-pi).

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