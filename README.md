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

**Prerequisites**: Install [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) and login:
```powershell
az login
az account set --subscription "Your-Subscription-Name"
```

### 1. Create Resource Group, IoT Hub, and Device Provisioning Service

```powershell
# Set variables
$resourceGroup = "skittlesorter-rg"
$location = "eastus"
$iotHubName = "skittlesorter-hub"
$dpsName = "skittlesorter-dps"

# Create resource group
az group create --name $resourceGroup --location $location

# Create IoT Hub (Standard tier required for DPS)
az iot hub create `
  --name $iotHubName `
  --resource-group $resourceGroup `
  --sku S1 `
  --location $location

# Create Device Provisioning Service
az iot dps create `
  --name $dpsName `
  --resource-group $resourceGroup `
  --location $location

# Link IoT Hub to DPS
$iotHubConnectionString = az iot hub connection-string show `
  --hub-name $iotHubName `
  --query connectionString -o tsv

az iot dps linked-hub create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --connection-string $iotHubConnectionString `
  --location $location

# Get DPS ID Scope (save this for appsettings.json)
az iot dps show --name $dpsName --resource-group $resourceGroup --query properties.idScope -o tsv
```

### 2. Create Azure Device Registry with Credential Policy

The certificate issuance feature requires Azure Device Registry (ADR) with a credential policy:

```powershell
$adrNamespace = "skittlesorter-adr"
$credentialPolicyName = "cert-policy"

# Create ADR namespace
az iot device-registry namespace create `
  --name $adrNamespace `
  --resource-group $resourceGroup

# Create credential policy for certificate issuance
az iot device-registry credential-policy create `
  --namespace-name $adrNamespace `
  --credential-policy-name $credentialPolicyName `
  --resource-group $resourceGroup `
  --certificate-type ECC `
  --validity-period-days 30
```

**Note**: You can use `RSA` instead of `ECC`. Adjust validity period as needed.

### 3. Create Enrollment Group

Choose one of the following attestation methods:

#### Option A: Symmetric Key Attestation (Simpler)

```powershell
$enrollmentGroupName = "skittlesorter-group"

# Create enrollment group with symmetric key attestation
az iot dps enrollment-group create `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --attestation-type symmetrickey `
  --credential-policy-name $credentialPolicyName `
  --credential-policy-namespace $adrNamespace `
  --provisioning-status enabled

# Get the primary key (save this for appsettings.json)
az iot dps enrollment-group show `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id $enrollmentGroupName `
  --query attestation.symmetricKey.primaryKey -o tsv
```

**Skip to Section 4 (Configure Device Credentials)** if using symmetric key attestation.

#### Option B: X.509 Certificate Attestation (More Secure)

This project also supports **X.509 intermediate attestation** with a proper certificate hierarchy:

```
Root CA (verified in DPS)
  └─ Intermediate CA (verified in DPS, used in enrollment)
      └─ Device Certificate (signed by intermediate)
```

**Why This Hierarchy?**

- **Root CA**: Trust anchor, verified via proof-of-possession (PoP), can be kept offline
- **Intermediate CA**: Operational signer, signs device certificates, can be rotated/revoked
- **Device Cert**: Device identity, authenticates to DPS, signed by intermediate

**⚠️ Important: Both Root AND Intermediate Must Be Verified**

DPS requires **proof-of-possession verification** for **BOTH** the root and intermediate certificates. If the intermediate is not verified, provisioning will fail with:

```
401 - CA certificate not found
```

We discovered this the hard way! Even if thumbprints match and the chain is correct, DPS will reject the device if the intermediate CA is not verified.

#### Clean Up Old Resources (If Any)

Before running the setup script for the first time (or re-running after changes), clean up any existing certificates and enrollments:

```powershell
# Delete existing enrollment group (if any)
az iot dps enrollment-group delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --enrollment-id skittlesorter-group

# Delete old certificates from DPS (if any)
az iot dps certificate delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name skittlesorter-root `
  --etag "*"

az iot dps certificate delete `
  --dps-name $dpsName `
  --resource-group $resourceGroup `
  --certificate-name skittlesorter-intermediate `
  --etag "*"

# Delete local certificate files
Remove-Item -Recurse -Force certs/
```

#### Automated Certificate Setup

Run the provided PowerShell script to automatically generate certificates and configure DPS:

```powershell
pwsh ./setup-x509-attestation.ps1 `
  -RegistrationId skittlesorter `
  -EnrollmentGroupId skittlesorter-group `
  -DpsName $dpsName `
  -ResourceGroup $resourceGroup `
  -CredentialPolicy $credentialPolicyName
```

**What This Script Does:**

1. **[1/8]** Creates certificate directory structure (`certs/root`, `certs/ca`, `certs/device`, `certs/issued`)
2. **[2/8]** Generates a complete certificate hierarchy:
   - **Root CA**: Self-signed, 4096-bit RSA, 10-year validity, `pathlen:1`
   - **Intermediate CA**: Signed by root, 4096-bit RSA, 5-year validity, `pathlen:0`
   - **Device Certificate**: Signed by intermediate, 2048-bit RSA, 1-year validity, `extendedKeyUsage=clientAuth`
3. **[3/8]** Extracts thumbprints for all certificates
4. **[4/8]** Uploads root CA to DPS and verifies ownership via proof-of-possession:
   - Generates verification code
   - Creates verification certificate with CN=\<code\>, signed by root
   - Verifies ownership (proves you control the root private key)
5. **[5/8]** Uploads intermediate CA to DPS and verifies ownership via proof-of-possession:
   - Generates verification code
   - Creates verification certificate with CN=\<code\>, signed by intermediate
   - Verifies ownership (proves you control the intermediate private key)
6. **[6/8]** Creates enrollment group with CA reference:
   - Uses `--ca-name` (references uploaded intermediate by name)
   - Links to credential policy for CSR-based certificate issuance
   - Sets intermediate as primary CA (it's the direct signer of device certificates)
7. **[7/8]** Updates `appsettings.json` with certificate paths
8. **[8/8]** Displays summary and verification status

**Key Files Generated:**

- `certs/root/root.pem`: Root CA certificate (upload & verify in DPS)
- `certs/root/root.key`: Root CA private key
- `certs/ca/ca.pem`: Intermediate CA certificate (upload & verify in DPS)
- `certs/ca/ca.key`: Intermediate CA private key
- `certs/ca/chain.pem`: Full chain (intermediate + root) for TLS presentation
- `certs/device/device.pem`: Device certificate (signed by intermediate)
- `certs/device/device.key`: Device private key
- `certs/device/device-full-chain.pem`: Full chain (device + intermediate + root)

**Verification Status Check:**

After the script completes, it will show:
```
Root CA isVerified: true
Intermediate CA isVerified: true
```

Both **must** be `true` for provisioning to work. If either is `false`, provisioning will fail with `401 - CA certificate not found`.

### 4. Configure Device Credentials

**⚠️ SECURITY**: Never commit `appsettings.json` to source control as it contains secrets!

1. **Copy the template**:
   ```bash
   cp appsettings.template.json appsettings.json
   ```

2. **Update `appsettings.json`** based on your chosen attestation method:

**For Symmetric Key Attestation:**
   - `IdScope`: From DPS Overview
   - `AttestationMethod`: Set to `"SymmetricKey"` (default)
   - `EnrollmentGroupKeyBase64`: The enrollment group primary key (from step 3)
   - `RegistrationId`: Your device identifier (e.g., `skittlesorter`)

**For X.509 Certificate Attestation:**
   - `IdScope`: From DPS Overview
   - `AttestationMethod`: Set to `"X509"`
   - `AttestationCertPath`: Path to device certificate (auto-set by setup script)
   - `AttestationKeyPath`: Path to device private key (auto-set by setup script)
   - `AttestationCertChainPath`: Path to chain file (auto-set by setup script)
   - `RegistrationId`: Your device identifier (e.g., `skittlesorter`)

See Configuration section below for complete settings.

**Note**: For X.509, the setup script automatically updates `appsettings.json` with the correct certificate paths.

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
      "AttestationMethod": "SymmetricKey",
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "EnrollmentGroupKeyBase64": "your-enrollment-group-primary-key-base64-encoded",
      "DeviceKeyBase64": null,
      "AttestationCertPath": "",
      "AttestationKeyPath": "",
      "AttestationCertChainPath": "",
      "CertificatePath": "certs/device.csr",
      "PrivateKeyPath": "certs/device.key",
      "IssuedCertificatePath": "certs/issued/issued.pem",
      "ApiVersion": "2025-07-01-preview",
      "SasExpirySeconds": 3600,
      "AutoGenerateCsr": true,
      "MqttPort": 8883,
      "EnableDebugLogging": true
    }
  },
  "Adr": {
    "Enabled": false,
    "SubscriptionId": "your-subscription-id",
    "ResourceGroupName": "your-resource-group",
    "NamespaceName": "your-adr-namespace",
    "DeviceUpdate": {
      "Enabled": false,
      "Attributes": {
        "deviceType": "machinery",
        "deviceOwner": "Operations",
        "deviceCategory": 16
      },
      "Tags": {
        "demo": "true"
      },
      "DeviceEnabled": true,
      "OperatingSystemVersion": "1.0.0"
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
- `AttestationMethod`: `"SymmetricKey"` (default) or `"X509"` for certificate-based authentication
- `ProvisioningHost`: DPS endpoint (usually `global.azure-devices-provisioning.net`)
- **For Symmetric Key Attestation:**
  - `EnrollmentGroupKeyBase64`: Base64-encoded enrollment group primary key
  - `DeviceKeyBase64`: Pre-computed device key (optional; auto-derived if null)
  - Leave `AttestationCertPath`, `AttestationKeyPath`, `AttestationCertChainPath` empty
- **For X.509 Certificate Attestation:**
  - `AttestationCertPath`: Path to device certificate (e.g., `certs/device/device.pem`)
  - `AttestationKeyPath`: Path to device private key (e.g., `certs/device/device.key`)
  - `AttestationCertChainPath`: Path to certificate chain file (e.g., `certs/ca/chain.pem` containing intermediate + root)
  - Leave `EnrollmentGroupKeyBase64` empty
- **Common Settings:**
  - `CertificatePath`: Path to CSR file for new certificate issuance
  - `PrivateKeyPath`: Path to private key for CSR
  - `IssuedCertificatePath`: Path to store the issued X.509 certificate
  - `ApiVersion`: DPS API version (use `2025-07-01-preview` for CSR-based provisioning)
  - `SasExpirySeconds`: SAS token TTL in seconds (default: 3600) - only used for symmetric key attestation
  - `AutoGenerateCsr`: Auto-generate CSR and private key if files don't exist (default: true)
  - `MqttPort`: MQTT port for DPS connection (default: 8883)
  - `EnableDebugLogging`: Enable verbose MQTT protocol logging for troubleshooting (default: false)

**Debugging DPS Connection Issues:**
Set `EnableDebugLogging: true` to see detailed MQTT protocol messages including:
- Connection parameters and credentials (lengths only, not actual keys)
- Message topics and payload sizes
- Response parsing and polling attempts
- Status updates during provisioning

Leave it `false` for cleaner production output.

#### Adr (Azure Device Registry)

Configuration for querying and updating device information in Azure Device Registry post-provisioning.

**Prerequisites**:
- Azure Device Registry namespace created in same resource group/subscription
- Your app's identity has `Reader` role on the ADR namespace (for list/get operations)
- Microsoft.DeviceRegistry provider registered for your subscription

**Options**:
- `Enabled`: Enable/disable ADR device listing and updates (default: false)
- `SubscriptionId`: Azure subscription ID containing the ADR namespace
- `ResourceGroupName`: Azure resource group containing the ADR namespace
- `NamespaceName`: ADR namespace name
- **DeviceUpdate**: Post-provisioning device attribute/tag updates
  - `Enabled`: Automatically update device after provisioning (default: false)
  - `Attributes`: Device attributes object (e.g., deviceType, deviceOwner, deviceCategory)
  - `Tags`: Device tags (key-value pairs for resource tagging)
  - `DeviceEnabled`: Enable/disable the device (default: true)
  - `OperatingSystemVersion`: Device OS version string (optional)

**Example ADR Configuration**:
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

**ADR Workflow**:
When enabled, after DPS provisioning completes:
1. **List Devices**: Query ADR namespace to list all devices (max 5 shown in logs)
2. **Update Device**: Optionally apply configured attributes, tags, enabled state, and OS version via REST PATCH
3. **Fetch Device**: Retrieve updated device details including location, etag, tags, and systemData
4. **Log Results**: Print device details to console with color-coded ADR section headers

## Running the Application

```bash
dotnet restore
dotnet build
dotnet run
```

The application will:
1. Load configuration from `appsettings.json`
2. Initialize hardware (or mock mode)
3. Provision device with Azure DPS:
   - **Symmetric Key**: Derives device key, generates SAS token, authenticates with DPS
   - **X.509**: Loads bootstrap certificate, authenticates with DPS using client certificate
4. DPS validates authentication and processes CSR
5. DPS issues new certificate via credential policy
6. Connect to Azure IoT Hub using newly issued certificate
7. Begin the sorting loop:
   - Pick up a Skittle
   - Read its color
   - Send telemetry to IoT Hub (detected colors only)
   - Position the chute based on color
   - Drop the Skittle
   - Repeat

## Architecture

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

Main application implementing the sorting logic:

- **ConfigurationLoader**: Loads all configuration from `appsettings.json` (mock mode, IoT Hub, DPS, servo positions, chute positions)
- **ServoController**: Manages servo positioning with configuration-driven angle mappings
- **TelemetryService**: Sends detected Skittle colors to IoT Hub
- **DpsInitializationService**: Orchestrates DPS provisioning and returns authenticated `DeviceClient`
- **SkittleSorterService**: Main sorting loop orchestration (color detection → servo movement → telemetry)
- **Program.cs**: Entry point loading all configurations and running the application

## Device Provisioning Flow

When the application starts with DPS enabled:

### Symmetric Key Attestation Flow

1. **Load DPS Configuration**: Read from `appsettings.json`
2. **Generate CSR (if needed)**: Auto-create device certificate signing request and private key (RSA 2048 by default)
3. **Derive Device Key**: Compute device-specific key using `HMACSHA256(base64Decode(enrollmentGroupKey), lowercase(registrationId))`
4. **Generate SAS Token**: Create signed MQTT credentials with DPS endpoint and URL-encoded resource URI
5. **MQTT Connection**: Connect to `global.azure-devices-provisioning.net:8883` with TLS
6. **MQTT Registration**: Publish registration request with base64-encoded DER CSR to `$dps/registrations/PUT/iotdps-register/`
7. **Poll for Status**: Wait for DPS to process (status: "assigning") and poll every 2 seconds
8. **Receive New Certificate**: Get assigned IoT Hub, device ID, and newly issued X.509 certificate chain via credential policy
9. **Parse Certificate**: Convert base64 certificates to PEM format, combine device cert with CSR private key
10. **Connect to IoT Hub**: Authenticate using newly issued X.509 certificate
11. **Begin Sorting**: Start main application loop

**Authentication Summary**:
- **Phase 1 (DPS)**: Symmetric key authentication (derived from enrollment group key)
- **Phase 2 (IoT Hub)**: X.509 certificate authentication (newly issued cert from credential policy)

### X.509 Certificate Attestation Flow

1. **Load DPS Configuration**: Read from `appsettings.json`
2. **Load Bootstrap Certificate**: Load device.pem, device.key, and chain.pem (intermediate + root)
3. **MQTT Connection**: Connect to `global.azure-devices-provisioning.net:8883` with TLS using X.509 client certificate authentication
4. **MQTT Registration**: Publish registration request with base64-encoded DER CSR to `$dps/registrations/PUT/iotdps-register/`
5. **DPS Validation**: DPS validates the certificate chain:
   - Device cert is signed by intermediate CA
   - Intermediate CA is verified in DPS (proof-of-possession)
   - Intermediate CA chains to verified root CA
   - If any certificate is not verified: **401 - CA certificate not found**
6. **Poll for Status**: Wait for DPS to process (status: "assigning") and poll every 2 seconds
7. **Receive New Certificate**: Get assigned IoT Hub, device ID, and newly issued X.509 certificate chain via credential policy
8. **Parse Certificate**: Convert base64 certificates to PEM format, combine device cert with CSR private key
9. **Connect to IoT Hub**: Authenticate using newly issued X.509 certificate
10. **Begin Sorting**: Start main application loop

**Authentication Summary**:
- **Phase 1 (DPS)**: X.509 certificate authentication (bootstrap cert signed by verified intermediate)
- **Phase 2 (IoT Hub)**: X.509 certificate authentication (newly issued cert from credential policy)

**Key Insight**: The bootstrap certificate (for X.509) or symmetric key is ONLY used to authenticate with DPS. Once the new certificate is issued via CSR, all subsequent IoT Hub communication uses the newly issued X.509 certificate.

For detailed protocol specifications, see **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)#how-it-works**.

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

### Troubleshooting DPS Issues

Common issues with DPS provisioning:

**401 Unauthorized - CA certificate not found (X.509 Attestation)**:
- **Root Cause**: Intermediate CA is not verified in DPS
- **Solution**: Run `az iot dps certificate show --certificate-name skittlesorter-intermediate` and check `isVerified: true`
- If false, the setup script should have verified it automatically - try rerunning the setup script
- Both root AND intermediate must be verified via proof-of-possession
- Check DPS certificates in Azure Portal → verify both show green checkmark

**401 Unauthorized (Symmetric Key Attestation)**:
- Wrong enrollment group key → Verify key matches DPS portal
- Registration ID mismatch → Check exact spelling (case-sensitive)
- Check diagnostic logs: `[KEY DERIVATION]` and `[SAS]`

**Certificate chain validation failed**:
- Device cert not signed by intermediate → Regenerate certificates using setup script
- Intermediate not chaining to verified root → Check issuer/subject match
- Run OpenSSL verification:
  ```powershell
  openssl verify -CAfile certs/ca/chain.pem certs/device/device.pem
  ```

**400 Bad Request**:
- CSR format incorrect → Must be base64 DER (not PEM with headers)
- API version mismatch → Ensure using `2025-07-01-preview`
- Enrollment group not linked to credential policy

**Certificate Not Issued**:
- Credential policy not configured
- ADR namespace issues
- Check Azure Portal DPS logs

**TLS Authentication Error**:
- Certificate not properly persisted
- Library handles this automatically with PFX export/reload

For detailed troubleshooting steps, see **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)#troubleshooting**.

## Security Best Practices

**⚠️ Important**: This repository excludes sensitive files via `.gitignore`:

**Never commit these files:**
- `appsettings.json` - Contains DPS enrollment keys and secrets
- `certs/` directory - Contains private keys and certificates
- `*.pem`, `*.key`, `*.pfx` files - Certificate and private key files

**Safe to commit:**
- `appsettings.template.json` - Template configuration (no secrets)
- Source code files
- Documentation

**Setup for new developers:**
1. Copy `appsettings.template.json` to `appsettings.json`
2. Fill in actual values from Azure Portal
3. Run the application - certificates are auto-generated on first run

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
  - ✅ Use Azure Key Vault for production
  - ✅ Use environment variables or secure configuration
  - ❌ Never hardcode or commit to Git
- **Certificates**: Device certificates are auto-generated and stored locally. Protect the `certs/` directory.
  - ✅ Use restrictive file permissions (chmod 600 on Linux)
  - ✅ Consider hardware security modules (HSM) for production
  - ✅ Rotate certificates before expiry (check validity period)
- **Connection Strings**: If using direct IoT Hub connection (without DPS), keep connection strings in environment variables or Azure Key Vault, never in code.
- **MQTT Port 8883**: Requires TLS/SSL. DPS endpoint is `global.azure-devices-provisioning.net:8883`.
- **Private Keys**: The private key (`certs/device.key`) must be protected. It is used to prove certificate ownership.

For detailed security guidance, see **[AzureDpsFramework/README.md](AzureDpsFramework/README.md)#security-considerations**.

## License

**Code**: MIT License (see LICENSE file)

**Hardware/3D Models**: Licensed under the same terms as the original [Candy Sorter repository](https://github.com/PTC-Education/Candy-Sorter/) (PTC Education)

You are free to use, modify, and distribute the code in this repository under the MIT License. The 3D printed sorter components are based on PTC Education's Candy Sorter design.
