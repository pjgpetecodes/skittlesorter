## **The Device Application**

> **CLI baseline:** The examples use Azure IoT CLI preview extension
> `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed against current Microsoft Learn guidance **5 November 2026**.
> [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

In this post, we'll walk through the complete IoT device application that uses DPS provisioning with CSR-based certificate issuance. Rather than building from scratch, if you've not already done so, we'll clone the repository, run the setup automation, configure the application, and explore how the major components work together.

### Current preview status

IoT Hub integration with Azure Device Registry (ADR) and certificate
management remains in **public preview** and isn't recommended for production.
The current getting-started guide supports creating a new Hub or connecting an
eligible existing Hub; connecting an existing Hub is permanent and can't be
reversed. The setup script in Part 3 creates a dedicated new Hub. DPS is
required for every IoT Hub preview scenario. The current supported regions are
Australia East, Japan East, Central US, and UK South. ADR integration and
certificate management are free during preview, while IoT Hub and DPS are
billed at standard rates.

Certificate management issues operational X.509 certificates after a device
authenticates with a separate onboarding credential. The certificate policy
belongs to an ADR intermediate CA and is selected on the DPS enrollment. ADR
synchronizes CA certificates with linked IoT Hubs and manages CA lifecycle and
rotation operations. SDK renewal APIs are available, but this application does
not implement automatic operational-certificate renewal. See Microsoft's [ADR connected-services
guide](https://learn.microsoft.com/en-gb/azure/iot/device-registry/get-started-azure-device-registry),
[CA hierarchy](https://learn.microsoft.com/en-gb/azure/iot/concept-managed-ca-hierarchy),
[managed CA setup](https://learn.microsoft.com/en-gb/azure/iot/how-to-create-policy?tabs=cli),
and [certificate-policy enrollment
guide](https://learn.microsoft.com/en-us/azure/iot/how-to-create-edit-certificate-policy?tabs=cli)
before creating or changing Azure resources.

### What's changed in this edition?

I've replaced the custom C# DPS provisioning protocol and the legacy
`Microsoft.Azure.Devices.Client` with Microsoft's unified
[`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4).
One `ConnectionClient` handles provisioning and the Hub connection;
`TelemetryClient` shares it for our Skittle readings. `AzureDpsFramework` stays
as a thin configuration, certificate-persistence, ADR ARM and session adapter.

Both standard symmetric-key/X.509 profiles and both CSR onboarding flows remain.
.NET 10, our existing Hub and Azure resources are unchanged. The SDK discovers
the assigned endpoint/profile through DPS, not a hostname constructed from the
Hub name. The sorter
now uses MQTTnet 5; the separate software-updates client stays on MQTTnet 4 and
its own DPS **update** protocol. That workflow isn't implemented by this SDK.

The new bounded `--cloud-self-test --config PATH` provisions, connects, sends
representative telemetry and disconnects without physical hardware. SDK retry
and reprovisioning are available, but automatic certificate renewal isn't
implemented. Microsoft's [SDK design and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified)
explain the new API. This is still a preview. See the
[Introduction](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1)
for the series-wide changes.

### Contents

1. [Introduction](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1)
2. [A DPS Primer](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2)
3. [Creating DPS, IoT Hub and ADR Instances](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3)
4. [Understanding X.509 and CSR Workflows](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4)
5. [The Device Application (this post)](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5)

### Why Keep a DPS Adapter?

> I no longer need to maintain security providers, SAS tokens, registration
> polling or a DPS MQTT transport in C#. The official unified SDK handles those.
> Our adapter still keeps configuration, CSR/key and issued-chain files durable,
> and ADR ARM metadata operations out of the sorting loop.
>
> **Note:** The cleanup commands in older versions of this post use a previous
> resource setup. For the refreshed deployment, follow the resource review and
> cleanup guidance in the Part 3 setup README.

### Application Architecture

```plaintext
┌─────────────────────────────────────────────────────────┐
│                     Program.cs                          │
│                   (Main Entry Point)                    │
└─────────────────────┬───────────────────────────────────┘
                      │
    ┌─────────────┬───┴─────────┬───────────────┐
    │             │             │               │
    ▼             ▼             ▼               ▼
┌─────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────┐
│ Config  │  │   DPS    │  │Telemetry │  │   Hardware   │
│ Loader  │  │   Init   │  │ Service  │  │   Drivers    │
└─────────┘  └────┬─────┘  └────┬─────┘  └──────────────┘
                  │             │
                  ▼             ▼
          ┌─────────────────────────┐
          │  Azure DPS Framework    │
          │  (Thin SDK adapter)     │
          └──────────┬──────────────┘
                     │
                     ▼
              ┌─────────────┐
              │   IoT Hub   │
              └─────────────┘

```

The adapter opens the SDK `ConnectionClient`; telemetry uses `TelemetryClient`
on the same connection. No second custom provisioning transport sits underneath.

### Getting Started

> **Already completed previous posts?** 
> 
> If you've already cloned the repository and run the setup scripts from the earlier posts, you can skip to [Step 3: Configure the Application](#step-3-configure-the-application).
> 
> The steps below are for those starting fresh or who need to set up the complete environment.

#### Step 1: Clone the Repository (If Not Already Done)

If you haven't already cloned the repository from earlier posts, get the complete project code:

**Clone the repository:**

```powershell
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter
```

#### Step 2: Create the Azure Resources

Follow our updated [Azure CLI walkthrough in Part 3](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/)
to create the ADR namespace, a **new** IoT Hub, and DPS in a supported region.
The commands are aligned with the current
[Microsoft Learn Azure CLI deployment method](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).
For this application's X.509 onboarding credentials, also complete the
bootstrap certificate and enrollment steps in
[Part 4](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/).

All IoT Hub preview scenarios require DPS. If using certificate management,
configure a device onboarding credential separately from the ADR-managed
operational issuer. Create an ADR intermediate CA and its certificate policy,
then select that CA and policy on the DPS enrollment. Connect DPS before the
Hub; ADR synchronizes CA certificates with linked Hubs. Keep the resulting DPS
ID Scope, hub hostname, and onboarding credential for the application
configuration.

#### Step 3: Configure the Application

Update `appsettings.json` with the values from your reviewed setup:

```json
{
  "Hardware": {
    "MockMode": {
      "EnableMockColorSensor": true,
      "EnableMockServos": true,
      "MockColorSequence": [
        "Red",
        "Green",
        "Yellow",
        "Purple",
        "Orange",
        "Red",
        "Green"
      ]
    }
  },
  "IoTHub": {
    "DpsProvisioning": {
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "IdScope": "YOUR_DPS_ID_SCOPE",
      "RegistrationId": "YOUR_DEVICE_REGISTRATION_ID",
      "AttestationMethod": "X509",
      "RequestOperationalCertificate": true,
      "AttestationCertPath": "scripts/certs/device/{RegistrationId}-device.pem",
      "AttestationKeyPath": "scripts/certs/device/{RegistrationId}-device.key",
      "AttestationCertChainPath": "scripts/certs/ca/{RegistrationId}-chain.pem",
      "CsrFilePath": "certs/device/{RegistrationId}-device.csr",
      "CsrKeyFilePath": "certs/device/{RegistrationId}-device.key",
      "IssuedCertFilePath": "certs/issued/{RegistrationId}-issued.pem",
      "ApiVersion": "2025-07-01-preview",
      "SasExpirySeconds": 3600,
      "AutoGenerateCsr": true,
      "MqttPort": 8883,
      "EnableDebugLogging": true
    },
    "DeviceId": "{RegistrationId}",
    "SendTelemetry": true,
    "TelemetryIntervalMs": 5000
  },
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "<YOUR_SUBSCRIPTION_ID>",
    "ResourceGroupName": "<YOUR_RESOURCE_GROUP>",
    "NamespaceName": "<YOUR_ADR_NAMESPACE_NAME>"
  }  
}
```

**Key Configuration Sections:**

-   **DpsProvisioning**: Bootstrap certificate paths and DPS connection details
-   **DeviceId**: Must match your registration ID
-   **IssuedCertFilePath**: Where the operational certificate chain from DPS will be saved
-   **Hardware.MockMode**: Enable hardware simulation for testing without physical devices

This is the X.509 + CSR profile from Part 4. For symmetric-key onboarding,
select `SymmetricKey` and supply `DeviceKeyBase64` (prefer a device-specific
key over a group master key). Set `RequestOperationalCertificate` to `false`
for standard symmetric-key or X.509 provisioning without issuance; the default
stays `true` for compatibility with the earlier CSR examples.

Keep the CSR, matching key and issued chain across restarts. Paths retain the
existing [configuration resolution rules](https://github.com/pjgpetecodes/skittlesorter/blob/main/docs/Configuration.md);
don't package those files with an
application release. The Part 3 setup script produces unified-compatible
configuration with symmetric-key onboarding and CSR issuance enabled; select
X.509 explicitly for this Part 4 profile.

#### Step 4: Run the Application

**Build and run:**

```powershell
dotnet run --project .\skittlesorter.csproj -- --config .\appsettings.json
```

**Illustrative CSR-profile output** (SDK diagnostics differ from the old custom logs):

```plaintext
Skittle Sorter Starting...

=== DPS Configuration ===
IdScope: 0ne00XXXXXX
RegistrationId: my-device
AttestationMethod: X509

=== Starting DPS Provisioning ===
Loaded bootstrap certificate: CN=my-device
Generated new RSA key pair for operational certificate.
Submitting registration request to DPS...
✓ Device assigned to: my-iothub-001.azure-devices.net
✓ Device ID: my-device

=== Saving Issued Certificate ===
Certificate Subject: CN=my-device
Issuer: CN=Microsoft-Managed-ICA-...
Valid from: 2026-02-07 10:00:00Z
Valid until: 2026-03-09 10:00:00Z
✓ Certificate chain saved to the configured IssuedCertFilePath

=== Connecting to IoT Hub ===
Connecting to: my-iothub-001.azure-devices.net
Device ID: my-device
✓ Connected to IoT Hub

=== Starting Sorting Loop ===

Detected: red (R:255 G:50 B:50 C:1200)
→ Telemetry sent: red (count: 1)
✓ red sorted

Detected: green (R:50 G:255 B:50 C:1180)
→ Telemetry sent: green (count: 1)
✓ green sorted

```

**What's happening:**

1.  **DPS Configuration** - Loads settings from `appsettings.json`
2.  **DPS Provisioning** - Authenticates with bootstrap certificate and generates CSR
3.  **Saving Certificate** - Receives operational certificate from DPS and saves it locally
4.  **Connecting to IoT Hub** - Uses operational certificate to establish MQTT connection
5.  **Sorting Loop** - Processes skittles and sends telemetry

#### A Bounded Cloud Check Before Moving Anything

I like to prove the connection before involving the motors:

```powershell
# Offline configuration check
dotnet run --project .\skittlesorter.csproj -- --self-test --config .\appsettings.json

# Real provisioning + Hub + representative telemetry, then disconnect
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.json
```

The cloud check has a two-minute bound and waits for one telemetry acknowledgement;
it doesn't initialize physical sensors or servos. It does contact Azure, so use
the intended subscription and identity. The helper
`scripts\iot-hub-device-registry\test-unified.ps1 -AppConfigPath PATH`
doesn't reset by default. Only use explicit `-ResetDevice` with matching resource
names to reset that exact device/registration; it never deletes a resource
group, enrollment group or CA. See the [cloud-test runbook](https://github.com/pjgpetecodes/skittlesorter/blob/main/scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test).

Reset also needs operator **Hub data-plane registry read/delete** permissions,
such as scoped IoT Hub Registry Contributor, not just resource-management access.
On a Hub with `DisableLocalAuth=true`, use Entra CLI authentication
(`--auth-type login`); SAS is disabled. The helper checks Hub registry access
before destructive work and stops if permission is missing. A successful
device connection doesn't grant the operator registry access, so don't change
RBAC unilaterally to force the reset through.

### Verify in the Portal

You should now be able to navigate to your resource group in the Azure Portal and see your device happily registered and showing as a first class citizen;

[![ADR device resource in the Azure portal](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-1.png)](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-1.png)

If you Navigate to your IoT Hub, and then to Device Management, and Devices, you should see your traditional IoT Hub Device present too;

[![IoT Hub device management view](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-2-1024x192.png)](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-2.png)

### Setting Up More Devices

Should you want to set up more devices, this is how you go about provisioning them.

Navigate to the Scripts folder and run the setup script with the following parameters;

```powershell
cd ./scripts
./setup-x509-attestation.ps1 -RegistrationId "<new-device-id>" -ReuseCa -CaRegistrationId "skittlesorter" -SkipEnrollment
```

Then, you can copy the following three files to your device to the scripts/certs folder;

-   `scripts/certs/device/<new-device-id>-device.pem`
-   `scripts/certs/device/<new-device-id>-device.key`
-   `scripts/certs/ca/<new-device-id>-chain.pem`

Then, along with your IdScope and other related settings, you need to modify the device's appsettings.json with the following values;

-   `RegistrationId = "<new-device-id>"`
-   `AttestationCertPath` to `<new-device-id>-device.pem`
-   `AttestationKeyPath` to `<new-device-id>-device.key`
-   `AttestationCertChainPath` to `<new-device-id>-chain.pem`

Finally, you can start the application, and you should have a newly provisioning device in your DPS, IoT Hub and in ADR along with the portal arm resource.

[![Additional device registered in DPS, IoT Hub, and ADR](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image.png)](https://www.petecodes.co.uk/wp-content/uploads/2026/02/image.png)

### Troubleshooting Common Issues

**Problem: "Failed to load bootstrap certificate"**

```plaintext
Error: The system cannot find the file specified.
at System.Security.Cryptography.X509Certificates.X509Certificate2..ctor
```

**Solution:**

-   Verify `AttestationCertPath` in `appsettings.json` points to correct location
-   Check if you ran the setup script from the `scripts/` directory
-   Ensure certificates exist: `./scripts/certs/device/device.pem`

**Check certificate files:**

```powershell
Test-Path "./scripts/certs/device/<YOUR_DEVICE_REGISTRATION_ID>-device.pem"
Test-Path "./scripts/certs/device/<YOUR_DEVICE_REGISTRATION_ID>-device.key"
```

**Problem: "DPS registration failed: Unauthorized"**

```plaintext
DPS initialization failed: Registration failed with status: Unauthorized
```

**Solution:**

-   Verify your enrollment group exists in DPS
-   Check that CA certificates are uploaded and **verified** in DPS (see [Understanding X.509 and CSR Workflows](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/))
-   Confirm `IdScope` in `appsettings.json` matches DPS

**Verify enrollment group:**

```powershell
az iot dps enrollment-group show `
  --dps-name "my-dps-001" `
  --resource-group "my-iot-rg" `
  --enrollment-id "my-device-group"
```

**Verify CA certificates are verified:**

```powershell
az iot dps certificate show `
  --dps-name "my-dps-001" `
  --resource-group "my-iot-rg" `
  --certificate-name "my-device-root" `
  --query "properties.isVerified"
```

**Problem: "Failed to connect to IoT Hub"**

```powershell
Error: The remote certificate is invalid according to the validation procedure
```

**Solution:**

-   Check that DPS is linked to IoT Hub
-   Verify IoT Hub hostname matches assigned hub
-   Ensure operational certificate was saved correctly

**Verify DPS linked hub:**

```powershell
az iot dps linked-hub list `
  --dps-name "my-dps-001" `
  --resource-group "my-iot-rg"
```

**Problem: "Device already provisioned" but can't connect**

If you see the operational certificate exists but connection fails:

Check the selected profile, saved chain, matching CSR key and assigned Hub first.
Don't delete a shared certificate directory to fix one device. Use the reviewed
exact-device cloud-test reset only when you intend to reprovision that identity.

**Problem: Missing `appsettings.json` configuration**

```plaintext
Unhandled exception. System.Exception: IoTHub config missing
```

**Solution:**

-   Ensure `appsettings.json` exists in project root
-   Copy from the safe X.509 template if needed:

```powershell
Copy-Item ".\src\configuration\appsettings.x509.template.json" ".\appsettings.json"
```

**Edit with your values:**

```powershell
notepad appsettings.json
```

**Problem: Certificate expired or renewal needed**

```plaintext
Certificate Valid until: 2026-01-15 10:00:00Z (expired)
```

**Solution:**

-   Check the certificate policy, persisted chain and matching private key.
-   Operational certificate validity is set by the ADR certificate policy.
-   There is no automatic renewal scheduler in this application, and no
    service-wide seven-day renewal requirement. See the [current certificate-policy
    guide](https://learn.microsoft.com/en-us/azure/iot/how-to-create-edit-certificate-policy?tabs=cli)
    and the service's [renewal documentation](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview).

The SDK offers optional IoT Hub renewal APIs without DPS reprovisioning.
Using those safely needs a deliberate lifecycle implementation; deleting a
certificate isn't the same thing. Preserve keys and chains while diagnosing.

**Still having issues?**

1.  Check Azure resources are properly configured ([Creating DPS, IoT Hub and ADR Instances](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/))
2.  Verify certificate and CSR setup ([Understanding X.509 and CSR Workflows](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/))
3.  Review Microsoft's [ADR connected-services guide](https://learn.microsoft.com/en-gb/azure/iot/device-registry/get-started-azure-device-registry) and [certificate-policy enrollment guide](https://learn.microsoft.com/en-us/azure/iot/how-to-create-edit-certificate-policy?tabs=cli) for current resource and enrollment configuration
4.  Check DPS and IoT Hub logs in Azure Portal
5.  Enable verbose logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

### Understanding the Code Architecture

Now let's explore the major components that make this work.

#### Project Structure

```plaintext
skittlesorter/
├── src/
│   ├── Program.cs                      # Main entry point
│   ├── comms/
│   │   ├── DpsInitializationService.cs # DPS provisioning logic
│   │   └── TelemetryService.cs         # IoT Hub telemetry
│   ├── configuration/
│   │   └── ConfigurationLoader.cs      # Load appsettings.json
│   └── drivers/
│       ├── SkittleSorterService.cs     # Main business logic
│       ├── TCS3472x.cs                 # Color sensor driver
│       └── ServoController.cs          # Servo control
├── AzureDpsFramework/                  # Thin SDK/session adapter
│   ├── DpsConfiguration.cs             # Existing profile configuration
│   ├── CertificateManager.cs           # CSR, private key and chain persistence
│   └── Adr/                           # ADR ARM metadata operations
└── appsettings.json                    # Device configuration
```

#### Main Program Flow (src/Program.cs)

The main program still loads configuration, initializes mock or real hardware
for a normal run, opens the cloud session and runs pick → detect → sort → drop.
The cloud-self-test takes a separate bounded path and never initializes physical
hardware. Shutdown disconnects the SDK session gracefully.

#### DPS Initialization Service (src/comms/DpsInitializationService.cs)

This service selects the configured attestation/issuance profile and delegates
session creation to `AzureDpsFramework`. `ConnectionClient` handles the actual
DPS registration, credentials and Hub connection.

For CSR profiles, the adapter loads or creates the durable CSR/key pair,
supplies it to the SDK, then persists the returned operational chain with the
matching key. The `.connection.json` assignment sidecar alongside the issued PEM
lets restarts restore the operational certificate and assigned Hub. A required-
certificate wrapper rejects a missing issued chain before Hub authentication.
For standard profiles, the adapter doesn't request a new certificate.
The onboarding certificate and operational certificate remain different
credentials; moving to an SDK hasn't merged their roles.

#### Thin AzureDpsFramework Adapter (AzureDpsFramework/)

I've kept the pieces specific to this application: configuration, device-key
derivation where needed, certificate files, optional ADR ARM metadata helpers
and the unified-session adapter. The old security-provider, transport,
registration and SAS-token protocol classes are removed, not wrapped.

The `UnifiedDeviceSession` adapter delegates MQTT to the SDK. Its coordinator
holds the updater's shared lock only during DPS sessions, including reprovisioning,
not for the lifetime of the Hub telemetry connection.

For the SDK API itself, follow the [official unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).
For our file/configuration boundary, read the
[adapter README](https://github.com/pjgpetecodes/skittlesorter/blob/main/AzureDpsFramework/README.md).

The smaller boundary looks like this (with a loaded `DpsConfiguration` and
cancellation token):

```csharp
using var session = UnifiedDeviceSession.Create(dpsConfiguration);
try
{
    await session.ConnectAsync(cancellationToken);
    // TelemetryService receives session.Telemetry, the SDK TelemetryClient.
}
finally
{
    await session.DisconnectAsync();
}
```

`session.Connection` is the SDK `ConnectionClient`; `session.Telemetry` shares
that connection. There's no legacy `DeviceClient` or custom register/poll loop.

#### Telemetry Service (src/comms/TelemetryService.cs)

`TelemetryService` now uses the SDK's `TelemetryClient` on the same connection.
It preserves the application's JSON telemetry and message-property contract;
we aren't changing cloud routes or introducing a second Hub connection.
The optional session-count feature remains independent of this SDK migration.

#### Certificate Renewal

SDK retry/reconnection and reprovisioning are available through `ConnectionClient`.
They aren't an automatic certificate-renewal scheduler. Optional IoT Hub renewal
APIs are also available, but I haven't wired automatic operational-certificate
renewal into this application. The old “delete the certificate seven days
before expiry” snippet wasn't a complete lifecycle implementation, so I've
removed it. Durable replacement and recovery need their own tests.

### Azure Device Registry (ADR) Integration

Azure Device Registry provides device identity and metadata management that works with DPS and IoT Hub.

#### Key ADR Features

**🔹 Device Metadata**

-   Registry properties: manufacturer, model, hardware revision and software revision
-   Tags for custom labels such as hardware ID, location, tenant and environment
-   Enablement state: `Enabled` or `Disabled`

**🔹 Credential Management**

-   Policies for operational-certificate issuance
-   Certificate issuance, renewal, and revocation
-   CA lifecycle management, including automatic CA rotation

**🔹 Multi-Hub Support**

-   A namespace can be linked to multiple IoT Hubs
-   Devices are represented as Azure resources in the namespace
-   Query and govern resources across hubs sharing a namespace

Don't infer that moving a device between hubs avoids reprovisioning; follow
the current DPS and IoT Hub documentation for device migration behavior.

#### ADR Client Implementation

The project includes an ADR client for device management (AzureDpsFramework/Adr/AdrDeviceRegistryClient.cs):

```csharp
using Azure.Identity;
using AzureDpsFramework.Adr;

// Create ADR client (uses DefaultAzureCredential)
var adrClient = new AdrDeviceRegistryClient();

// List all devices in namespace
var devices = await adrClient.ListDevicesAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "my-iot-rg",
    namespaceName: "my-adrnamespace-uniquesuffix"
);

Console.WriteLine($"Found {devices.Count} devices:");
foreach (var device in devices)
{
    Console.WriteLine($"  - {device.Name}: {device.Properties}");
}

// Get specific device details
var deviceDetails = await adrClient.GetDeviceAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "my-iot-rg",
    namespaceName: "my-adrnamespace-uniquesuffix",
    deviceName: "my-device"
);

if (deviceDetails != null)
{
    Console.WriteLine($"\nDevice: {deviceDetails.Name}");
    Console.WriteLine($"Properties: {deviceDetails.Properties}");
    
    // Display tags
    if (deviceDetails.Tags.ValueKind == System.Text.Json.JsonValueKind.Object)
    {
        Console.WriteLine("Tags:");
        foreach (var tag in deviceDetails.Tags.EnumerateObject())
        {
            Console.WriteLine($"  {tag.Name}: {tag.Value}");
        }
    }
}
```

#### Configuration-Driven ADR Updates

Enable automatic device metadata updates in `appsettings.json`:

```json
{
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "your-subscription-id",
    "ResourceGroupName": "my-iot-rg",
    "NamespaceName": "my-adrnamespace-uniquesuffix",
    "DeviceUpdate": {
      "Enabled": true,
      "Attributes": {
        "manufacturer": "Raspberry Pi Foundation",
        "model": "Raspberry Pi 4B",
        "hardwareRevision": "1.4",
        "softwareRevision": "1.0.5"
      },
      "Tags": {
        "location": "factory-1",
        "department": "manufacturing",
        "hardwareId": "RPI4B-12345678"
      },
      "DeviceEnabled": true
    }
  }
}
```

The application automatically updates device metadata after provisioning.

There's one API detail worth calling out: the current ARM resource is
`namespaces/registryDevices` (`2026-11-02-preview`), and it doesn't accept an
arbitrary `attributes` object. Our `Attributes` configuration name is retained,
but only exact keys `manufacturer`, `model`, `hardwareRevision` and
`softwareRevision` are accepted and mapped to properties. Other keys fail
explicitly; put custom labels in string-valued `Tags`, as with `hardwareId` above.

`DeviceEnabled` maps to `enablementState: "Enabled"` or `"Disabled"`.
The older `OperatingSystemVersion` configuration name is still accepted and maps
to `softwareRevision`; if both are supplied, it overrides
`Attributes.softwareRevision`. It isn't an ARM property of its own.

### Testing the Complete Solution

#### Test 1: Verify Device in IoT Hub

**Check device exists:**

```powershell
az iot hub device-identity show `
  --hub-name "my-iothub-001" `
  --device-id "my-device"
```

**Expected output:**

```json
{
  "deviceId": "my-device",
  "authentication": {
    "type": "certificateAuthority"
  },
  "connectionState": "Connected",
  "status": "enabled"
}
```

### Test 2: Monitor Telemetry

**Watch device-to-cloud messages:**

```powershell
az iot hub monitor-events `
  --hub-name "my-iothub-001" `
  --device-id "my-device"
```

**Expected output:**

```json
{
  "event": {
    "origin": "my-device",
    "payload": {
      "deviceId": "my-device",
      "timestamp": "2026-02-07T12:34:56Z",
      "color": "red",
      "count": 1
    }
  }
}
```

### Test 3: Query ADR Devices

**List devices:**

```powershell
az iot adr ns device list `
  --namespace "my-adrnamespace-uniquesuffix" `
  --resource-group "my-iot-rg" `
  --output table
```

**Expected output:**

```plaintext
Name        Location
----------  ----------
my-device   uksouth
```

Column selection can vary. Inspect JSON for `properties.enablementState` and
the supported metadata properties; custom labels are under the resource's `tags`.

### Test 4: Device Twin Operations

**Update desired properties:**

```powershell
az iot hub device-twin update `
  --hub-name "my-iothub-001" `
  --device-id "my-device" `
  --set properties.desired='{"telemetryInterval":3000,"enableSorting":true}'
```

The device receives the update and responds with reported properties.

### Production Deployment Checklist

#### ✅ Security

-   \[ \] Store enrollment keys in Azure Key Vault
-   \[ \] Use hardware security modules (TPM) for private keys
-   \[ \] Enable certificate pinning
-   \[ \] Implement certificate revocation checks
-   \[ \] Use separate enrollment groups for dev/staging/prod
-   \[ \] Rotate enrollment group keys periodically

#### ✅ Reliability

-   \[ \] Implement exponential backoff for retries
-   \[ \] Handle network disconnections gracefully
-   \[ \] Monitor certificate expiration
-   \[ \] Implement automatic certificate renewal
-   \[ \] Log all provisioning events
-   \[ \] Set up alerting for failed provisioning

#### ✅ Monitoring

-   \[ \] Enable Azure Monitor for IoT Hub
-   \[ \] Configure diagnostic logs for DPS
-   \[ \] Track device connection metrics
-   \[ \] Monitor telemetry delivery rates
-   \[ \] Set up alerts for certificate expiration
-   \[ \] Track ADR API usage and errors

#### ✅ Configuration

-   \[ \] Use environment-specific appsettings files
-   \[ \] Store secrets in Key Vault or environment variables
-   \[ \] Document all configuration parameters
-   \[ \] Validate configuration on startup
-   \[ \] Implement configuration hot-reload

#### ✅ Testing

-   \[ \] Unit tests for CSR generation
-   \[ \] Integration tests for DPS provisioning
-   \[ \] E2E tests for telemetry flow
-   \[ \] Load testing for concurrent provisioning
-   \[ \] Certificate renewal testing

### Safe Resource Cleanup

The older cleanup commands in this article refer to a previous resource and
identity setup; don't use them for the refreshed Part 3 deployment. Stop the
sorter first. If you used the Part 3 setup script, review the resource group
and delete it only if it contains dedicated test resources you intend to
remove. The [setup README](https://github.com/pjgpetecodes/skittlesorter/blob/main/scripts/iot-hub-device-registry/README.md)
describes the resources and costs. Connecting an existing Hub or DPS instance
to a namespace is permanent, so don't use resource-group deletion as a
substitute for reviewing what is linked and billable.

### Key Takeaways

✅ **Unified SDK** - One connection for provisioning and Hub feature clients
✅ **Two-Phase Authentication** - Bootstrap cert for DPS, operational cert for IoT Hub  
✅ **CSR-Based Issuance** - Private key never leaves device  
✅ **Thin Adapter** - Configuration, persistence and ADR ARM helpers, not a custom DPS protocol\
✅ **Certificate Persistence** - Durable CSR/key and issued chain; no automatic renewal claim\
✅ **Mock Hardware Support** - Test without physical devices

### What We Accomplished

✅  Cloned the repository and ran full setup automation  
✅ Configured the device application with Azure resources  
✅ Understood the main program flow and component architecture  
✅ Replaced the custom provisioning protocol with the official unified SDK
✅ Learned how CSR-based provisioning works end-to-end  
✅ Integrated with Azure Device Registry for metadata management  
✅ Tested the complete solution with telemetry monitoring  
✅ Reviewed the documented preview limitations and production warning

### Further Learning

-   **Azure IoT Hub Documentation**: [learn.microsoft.com/azure/iot-hub](https://learn.microsoft.com/azure/iot-hub/)
-   **Device Provisioning Service**: [learn.microsoft.com/azure/iot-dps](https://learn.microsoft.com/azure/iot-dps/)
-   **Azure Device Registry**: [learn.microsoft.com/azure/iot/iot-device-registry-overview](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
-   **X.509 Certificates**: [learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview](https://learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview)
-   **ADR connected-services setup**: [Get started with Azure Device Registry](https://learn.microsoft.com/en-gb/azure/iot/device-registry/get-started-azure-device-registry)
-   **ADR CA hierarchy**: [Certificate authorities hierarchy](https://learn.microsoft.com/en-gb/azure/iot/concept-managed-ca-hierarchy)
-   **ADR certificate policy**: [Create a certificate policy and select it in a DPS enrollment](https://learn.microsoft.com/en-us/azure/iot/how-to-create-edit-certificate-policy?tabs=cli)

### Questions or Issues?

-   Open an issue on GitHub
-   Check the [Troubleshooting.md guide](https://github.com/pjgpetecodes/skittlesorter/blob/main/docs/Troubleshooting.md)
-   Review the [current Azure setup guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)

Thank you for following this series! Happy IoT building! 🚀
