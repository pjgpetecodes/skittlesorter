# Building the Device Application

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

[← Previous: Durable Device Credentials](05-building-dps-framework.md) | [Back to Introduction](00-introduction.md)

---

In this post, we'll walk through the complete IoT device application that uses DPS provisioning with CSR-based certificate issuance. Rather than building from scratch, we'll clone the repository, run the setup automation, configure the application, and explore how the major components work together.

## What's changed?

I've replaced the custom C# DPS provisioning protocol and
`Microsoft.Azure.Devices.Client` with the unified
[`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4).
`ConnectionClient` handles provisioning and the Hub connection; `TelemetryClient`
shares it for the Skittle readings. The sorter calls these SDK clients directly;
`AzureDpsFramework` supplies configuration, durable credentials and optional
ADR ARM helpers. Its existing session API remains for package compatibility.

Standard symmetric-key/X.509 profiles and both CSR onboarding flows remain.
.NET 10, our existing Hub and cloud resources are unchanged. The SDK discovers
the assigned endpoint/profile through DPS. MQTTnet 5 in the sorter is a
dependency upgrade, not a manual Hub migration. The separate updater
retains MQTTnet 4 and its own DPS **software-update** MQTT protocol, which the
unified SDK doesn't implement.

The bounded `--cloud-self-test --config PATH` provisions, connects, sends
representative telemetry and disconnects without physical hardware. SDK retry
and reprovisioning are available, as are optional IoT Hub renewal APIs, but this
app doesn't implement automatic operational-certificate renewal.

Read Microsoft's [SDK design and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).
The SDK and ADR certificate-management integration are still preview features;
see [What's Changed since the Previous Preview](07-whats-changed-since-previous-preview.md).

## Application Architecture

```
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
          │ Unified SDK clients     │
          │ + credential helpers    │
          └──────────┬──────────────┘
                     │
                     ▼
              ┌─────────────┐
              │   IoT Hub   │
              └─────────────┘
```

## Getting Started

> **Already completed previous posts?** If you've already cloned the repository and run the setup scripts from [Post 02](02-creating-azure-resources.md) or [Post 03](03-x509-and-csr-workflows.md), you can skip to [Step 3: Configure the Application](#step-3-configure-the-application). The steps below are for those starting fresh or who need to set up the complete environment.

## Step 1: Clone the Repository (If Not Already Done)

If you haven't already cloned the repository from earlier posts, get the complete project code:

**Clone the repository:**

```powershell
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter
```

## Step 2: Run Full Azure Setup (If Not Already Done)

If you haven't already run the Azure setup automation, follow the
[Part 3 setup](02-creating-azure-resources.md). It creates the ADR namespace,
Hub, DPS, managed operational issuer and policy, a demo bootstrap issuer,
X.509 enrollment and local configuration. The manual certificate walkthrough
in [Part 4](03-x509-and-csr-workflows.md) is optional.

## Step 3: Configure the Application

Use the config written by Part 3: `appsettings.002.json` by default, or the
path passed to `-AppConfigPath`. It already contains the DPS ID scope,
registration ID, bootstrap certificate paths and CSR settings. Mock hardware
is enabled, and the SDK discovers the assigned Hub through DPS. Review the
configuration before running it; don't copy private keys into source control.

**Key Configuration Sections:**

- **DpsProvisioning**: Bootstrap certificate paths and DPS connection details
- **DeviceId**: Must match your registration ID
- **IssuedCertFilePath**: Where the operational certificate chain from DPS will be saved
- **Hardware.MockMode**: Enable hardware simulation for testing without physical devices

This example uses X.509 + CSR. For symmetric-key onboarding, choose
`SymmetricKey` and supply `DeviceKeyBase64`; prefer a device-specific key over
distributing a group master key. Set `RequestOperationalCertificate` to `false`
for standard symmetric-key or X.509 provisioning without issuance. The default
stays `true` for compatibility with the earlier CSR examples.

Keep the CSR, matching key, issued chain and assignment sidecar across restarts,
outside application
releases. Paths retain the existing [configuration resolution rules](../docs/Configuration.md).
The Part 3 setup defaults to X.509 onboarding and CSR issuance.

## Step 4: Run the Application

**Build and run:**

```powershell
dotnet run --project .\skittlesorter.csproj -- --config .\appsettings.002.json
```

**Illustrative CSR-profile output** (SDK diagnostics differ from the old custom logs):

```
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

1. **DPS Configuration** - Loads the generated settings
2. **DPS Provisioning** - Authenticates with bootstrap certificate and generates CSR
3. **Saving Certificate** - Receives operational certificate from DPS and saves it locally
4. **Connecting to IoT Hub** - Uses operational certificate to establish MQTT connection
5. **Sorting Loop** - Processes skittles and sends telemetry

### A Bounded Cloud Check Before Moving Anything

I like to prove the connection before involving the motors:

```powershell
# Offline configuration validation
dotnet run --project .\skittlesorter.csproj -- --self-test --config .\appsettings.002.json

# Real provisioning + Hub + representative telemetry, then disconnect
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.002.json
```

The cloud check has a two-minute bound and waits for one telemetry acknowledgement.
It doesn't initialize physical sensors or servos, but it does contact Azure.
The operator helper
`scripts\iot-hub-device-registry\test-unified.ps1 -AppConfigPath PATH`
doesn't reset by default; only use `-ResetDevice` with matching resource names
to reset that exact device/registration. It never deletes a resource group,
enrollment group or CA. See the [runbook](../scripts/iot-hub-device-registry/README.md#bounded-unified-sdk-cloud-test).

Reset also needs operator **Hub data-plane registry read/delete** permissions,
such as scoped IoT Hub Registry Contributor, not just resource-management access.
On a Hub with `DisableLocalAuth=true`, use Entra CLI authentication
(`--auth-type login`); SAS is disabled. The helper checks Hub registry access
before destructive work and stops if permission is missing. A successful
device connection doesn't grant the operator registry access, so don't change
RBAC unilaterally to force the reset through.

### Troubleshooting Common Issues

**Problem: "Failed to load bootstrap certificate"**

```
Error: The system cannot find the file specified.
at System.Security.Cryptography.X509Certificates.X509Certificate2..ctor
```

**Solution:**
- Check `AttestationCertPath`, `AttestationKeyPath` and `AttestationCertChainPath`
  in the generated config. If you moved it to another device, update the paths.

**Check certificate files:**

```powershell
$dps = (Get-Content .\appsettings.002.json -Raw | ConvertFrom-Json).IoTHub.DpsProvisioning
Test-Path -LiteralPath $dps.AttestationCertPath
Test-Path -LiteralPath $dps.AttestationKeyPath
Test-Path -LiteralPath $dps.AttestationCertChainPath
```

**Problem: "DPS registration failed: Unauthorized"**

```
DPS initialization failed: Registration failed with status: Unauthorized
```

**Solution:**
- Verify your enrollment group exists in DPS
- Check that CA certificates are uploaded and **verified** in DPS (see [Post 03](03-x509-and-csr-workflows.md))
- Confirm `IdScope` in `appsettings.json` matches DPS

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

```
Error: The remote certificate is invalid according to the validation procedure
```

**Solution:**
- Check that the ADR namespace links to DPS and IoT Hub have succeeded
- Verify IoT Hub hostname matches assigned hub
- Ensure operational certificate was saved correctly

**Verify the ADR namespace links:**

```powershell
az iot adr ns link dps list `
  --namespace "my-adr-namespace" `
  --resource-group "my-iot-rg"
az iot adr ns link hub list `
  --namespace "my-adr-namespace" `
  --resource-group "my-iot-rg"
```

`az iot dps linked-hub list` checks the separate classic allocation links; an
empty result doesn't mean these ADR namespace links are missing.

**Problem: "Device already provisioned" but can't connect**

If you see the operational certificate exists but connection fails:

Check the selected profile, saved chain, matching CSR key and assigned Hub first.
Don't delete a shared certificate directory to fix one identity. Use the reviewed
exact-device cloud-test reset only when you intend to reprovision it.

**Problem: Missing application configuration**

```
Unhandled exception. System.Exception: IoTHub config missing
```

**Solution:**
- Check that the generated config exists and `--config` points to it. Part 3
  defaults to `appsettings.002.json`; `-AppConfigPath` changes that location.
- If it was lost, inspect the existing enrollment and credentials before
  rerunning setup. The script refuses to overwrite an enrollment or private key.

**Problem: Certificate expired or renewal needed**

```
Certificate Valid until: 2026-01-15 10:00:00Z (expired)
```

**Solution:**
- Check the policy, saved chain and matching private key.
- Certificate validity is controlled by the ADR policy.
- The SDK offers optional IoT Hub renewal APIs, but this application has no
  automatic renewal scheduler. Deleting a certificate isn't that renewal API.

**Still having issues?**

1. Check Azure resources are properly configured ([Post 02](02-creating-azure-resources.md))
2. Verify certificates were created and verified ([Post 03](03-x509-and-csr-workflows.md))
3. Review enrollment group configuration ([Post 04](04-configuring-enrollment-groups.md))
4. Check DPS and IoT Hub logs in Azure Portal
5. Enable verbose logging in `appsettings.json`:

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

## Understanding the Code Architecture

Now let's explore the major components that make this work.

### Project Structure

```
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
├── AzureDpsFramework/                  # Configuration and credential helpers
│   ├── DpsConfiguration.cs             # Existing profile configuration
│   ├── CertificateManager.cs           # CSR, private key and chain persistence
│   └── Adr/                           # ADR ARM metadata operations
└── appsettings.json                    # Device configuration
```

### Main Program Flow ([src/Program.cs](../src/Program.cs))

The main program still loads configuration, initializes mock or real hardware
for a normal run, opens the cloud session and runs pick → detect → sort → drop.
The cloud-self-test takes a separate bounded path and never initializes physical
hardware. Shutdown disconnects the SDK session gracefully.

### DPS Initialization Service ([src/comms/DpsInitializationService.cs](../src/comms/DpsInitializationService.cs))

This service creates the SDK's `ConnectionClient` and `TelemetryClient` directly.
`ConnectionClient` handles DPS registration and the Hub connection.

For CSR profiles, the identity helper loads or creates the durable CSR/key pair,
supplies it to the SDK, then persists the returned operational chain with the
matching key. The `.connection.json` assignment sidecar alongside the issued PEM
lets restarts restore the operational certificate and assigned Hub. A required-
certificate wrapper rejects a missing issued chain before Hub authentication.
For standard profiles, it doesn't request a new certificate.
The onboarding and operational credentials still have separate roles.

### Application-specific helpers ([AzureDpsFramework/](../AzureDpsFramework/))

I've kept the application-specific pieces: configuration, device-key derivation
where needed, certificate files and optional ADR ARM metadata helpers.
The old session API is retained for existing package consumers but isn't used
by the sorter. The old security-provider, transport, registration
and SAS-token protocol classes are removed, not wrapped.

The MQTT coordinator delegates to the SDK and holds the
updater's shared lock only during DPS sessions, including reprovisioning, not
for the lifetime of the Hub telemetry connection.

For SDK API examples, use the [official unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).
For our configuration/file boundary, see the [helper README](../AzureDpsFramework/README.md).

For the default X.509 + CSR profile, our helper prepares the SDK inputs using
the same callback-based pattern as Microsoft's authentication sample:

```csharp
var authentication = new X509AuthenticationProvider(
    bootstrapCertificate, null, prepareCertificate, certificateChain: bootstrapChain);
var provisioning = new ProvisioningSettings(idScope)
{
    RegistrationId = registrationId,
    CertificateSigningRequest = new ProvisioningCertificateSigningRequest(operationalKey, csr)
};
```

Here `csr` is the base64-encoded DER signing request, and `operationalKey` is
the persisted key that signed it. `prepareCertificate` validates the returned
chain against that key and saves it; the private key stays on the device. The
application wraps the provider with a guard so a CSR profile cannot fall back
to the bootstrap credential if DPS returns no certificate.

The essential SDK calls look like this (with loaded configuration, the
coordinated MQTT options and a cancellation token):

```csharp
using var identity = DeviceProvisioningIdentity.Create(dpsConfiguration);
using var connection = new ConnectionClient(connectionOptions, identity.LoadContext());
using var telemetry = new TelemetryClient(connection);
var context = await connection.ProvisionAndConnectAsync(
    identity.Provisioning, identity.Authentication, cancellationToken: cancellationToken);
identity.SaveContext(context);
// Send telemetry using telemetry, then disconnect.
await connection.DisconnectAsync(cancellationToken: cancellationToken);
```

The actual initialization service also cancels the SDK's retry when the
required-certificate guard reports an issuance failure. The identity helper
uses the SDK certificate callback to validate and save the issued chain before
Hub authentication. The SDK samples keep the key in memory; our files preserve
the identity across restarts. There's no custom register/poll loop.

### Telemetry Service ([src/comms/TelemetryService.cs](../src/comms/TelemetryService.cs))

`TelemetryService` now uses `TelemetryClient` on the same connection. It preserves
the application's JSON telemetry and message-property contract. The optional
session-count feature is separate from this migration; no Hub routes or
resource topology need to change.

### Certificate Renewal

SDK retry/reconnection and reprovisioning are available through `ConnectionClient`.
They aren't an automatic certificate-renewal scheduler. Optional IoT Hub renewal
APIs are also available, but I haven't wired automatic operational-certificate
renewal into this application. The old “delete the certificate seven days
before expiry” snippet wasn't a complete lifecycle implementation, so I've
removed it. Durable replacement and recovery need their own tests.

## Azure Device Registry (ADR) Integration

Azure Device Registry provides device identity and metadata management that works with DPS and IoT Hub.

### Key ADR Features

**🔹 Device Metadata**
- Registry properties: manufacturer, model, hardware revision and software revision
- Tags for custom labels such as hardware ID, location, tenant and environment
- Enablement state: `Enabled` or `Disabled`

**🔹 Credential Management**
- Credential policies for certificate issuance
- Automated lifecycle management
- Certificate rotation

**🔹 Multi-Hub Support**
- Device identity independent of IoT Hub
- Move devices between hubs without re-provisioning
- Query devices across multiple hubs

### ADR Client Implementation

The project retains its separate ADR ARM client for device management ([AzureDpsFramework/Adr/AdrDeviceRegistryClient.cs](../AzureDpsFramework/Adr/AdrDeviceRegistryClient.cs)):

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

### Configuration-Driven ADR Updates

Enable automatic device metadata updates in `appsettings.json`:

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

## Testing the Complete Solution

### Test 1: Verify Device in IoT Hub

**Check device exists:** This requires Hub registry read permission for your
signed-in Entra identity; resource-management access alone isn't sufficient.

```powershell
az iot hub device-identity show `
  --hub-name "my-iothub-001" `
  --device-id "my-device" `
  --auth-type login
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

The bounded cloud self-test waits for a representative telemetry acknowledgement:

```powershell
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.002.json
```

For an interactive view of messages, use Azure IoT Explorer and its Hub
connection string. This is separate from the device's X.509 authentication.

You can also watch incoming messages from a terminal using the Azure IoT CLI
extension installed by the setup script:

```powershell
az iot hub monitor-events `
  --hub-name "my-iothub-001" `
  --device-id "my-device"
```

Replace the names with your Hub and registration ID. Start monitoring before
running the sorter, and press Ctrl+C to stop. The CLI uses a Hub shared-access
policy to read telemetry and needs permission to retrieve its key; this is
service-side access, not the device's X.509 credentials. Protect policy keys
and use this option only on a Hub where shared access is enabled.

### Test 3: Query ADR Devices

**List devices:**

```powershell
az iot adr ns device list `
  --namespace "my-adrnamespace-uniquesuffix" `
  --resource-group "my-iot-rg" `
  --output table
```

**Expected output:**

```
Name        Location
----------  ----------
my-device   uksouth
```

Column selection can vary. Inspect JSON for `properties.enablementState` and
the supported metadata properties; custom labels are under the resource's `tags`.

The sample doesn't currently implement device twin desired-property handling.

## Production Deployment Checklist

### ✅ Security

- [ ] Store enrollment keys in Azure Key Vault
- [ ] Use hardware security modules (TPM) for private keys
- [ ] Enable certificate pinning
- [ ] Implement certificate revocation checks
- [ ] Use separate enrollment groups for dev/staging/prod
- [ ] Rotate enrollment group keys periodically

### ✅ Reliability

- [ ] Implement exponential backoff for retries
- [ ] Handle network disconnections gracefully
- [ ] Monitor certificate expiration
- [ ] Implement automatic certificate renewal
- [ ] Log all provisioning events
- [ ] Set up alerting for failed provisioning

### ✅ Monitoring

- [ ] Enable Azure Monitor for IoT Hub
- [ ] Configure diagnostic logs for DPS
- [ ] Track device connection metrics
- [ ] Monitor telemetry delivery rates
- [ ] Set up alerts for certificate expiration
- [ ] Track ADR API usage and errors

### ✅ Configuration

- [ ] Use environment-specific appsettings files
- [ ] Store secrets in Key Vault or environment variables
- [ ] Document all configuration parameters
- [ ] Validate configuration on startup
- [ ] Implement configuration hot-reload

### ✅ Testing

- [ ] Unit tests for CSR generation
- [ ] Integration tests for DPS provisioning
- [ ] E2E tests for telemetry flow
- [ ] Load testing for concurrent provisioning
- [ ] Certificate renewal testing

## Repeat Tests Without Rebuilding the Environment

Use the bounded cloud test above first. If you deliberately need fresh
registration, the new helper requires explicit `-ResetDevice` and matching
resource names. It targets only that exact device/registration, not the
resource group, enrollment group or CA. The older `clean-test-start.ps1` helper
has a broader destructive scope and isn't a prerequisite for this SDK migration.
See [Clean Test Start](../docs/Clean-Test-Start.md) before any reset.

## Key Takeaways

✅ **Unified SDK** - One connection for provisioning and Hub feature clients
✅ **Two-Phase Authentication** - Bootstrap cert for DPS, operational cert for IoT Hub  
✅ **CSR-Based Issuance** - Private key never leaves device  
✅ **Thin Adapter** - Configuration, persistence and ADR ARM helpers, not a custom DPS protocol\
✅ **Certificate Persistence** - Durable CSR/key and issued chain; no automatic renewal claim\
✅ **ADR Integration** - Device metadata management and querying  
✅ **Preview Boundaries** - Local validation is separate from production readiness

## What We Accomplished

✅ Cloned the repository and ran full setup automation  
✅ Configured the device application with Azure resources  
✅ Understood the main program flow and component architecture  
✅ Replaced the custom provisioning protocol with the official unified SDK\
✅ Learned how CSR-based provisioning works end-to-end  
✅ Integrated with Azure Device Registry for metadata management  
✅ Tested the complete solution with telemetry monitoring  
✅ Reviewed preview limitations and outstanding cloud validation

## Further Learning

- **Azure IoT Hub Documentation**: [learn.microsoft.com/azure/iot-hub](https://learn.microsoft.com/azure/iot-hub/)
- **Device Provisioning Service**: [learn.microsoft.com/azure/iot-dps](https://learn.microsoft.com/azure/iot-dps/)
- **Azure Device Registry**: [learn.microsoft.com/azure/iot/iot-device-registry-overview](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
- **X.509 Certificates**: [learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview](https://learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview)

## Repository

Clone the complete project:

```bash
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter
```

### Questions or Issues?

- Open an issue on GitHub
- Check the [Troubleshooting.md](../docs/Troubleshooting.md) guide
- Review [Azure Setup documentation](../docs/Azure-Setup.md)

Thank you for following this series! Happy IoT building! 🚀

---

[← Previous: Building the Custom DPS Framework](05-building-dps-framework.md) | [Back to Introduction](00-introduction.md)
