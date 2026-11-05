## **The Device Application**

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

In this post, we'll walk through the complete IoT device application that uses DPS provisioning with CSR-based certificate issuance. Rather than building from scratch, if you've not already done so, we'll clone the repository, run the setup automation, configure the application, and explore how the major components work together.

### Current preview status

IoT Hub integration with Azure Device Registry (ADR) and certificate
management remains in **public preview** and isn't recommended for production.
Create a **new IoT Hub** for the integration; existing hubs have no upgrade
path. DPS is required for every IoT Hub preview scenario. The current supported
regions are Australia East, Japan East, Central US, and UK South. ADR
integration and certificate management are free during preview, while IoT Hub
and DPS are billed at standard rates.

Certificate management issues operational X.509 certificates after a device
authenticates with a separate onboarding credential. The current service
supports renewal and revocation of operational certificates and automatic CA
rotation. The renewal code later in this article describes this repository's
sample implementation, not the complete current Azure certificate-management
workflow. See the [current Learn deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)
before creating or changing Azure resources.

```wordpress-block
<!-- wp:heading {"textColor":"#f6f6f4"} -->
<h2 class="wp-block-heading has-f-6-f-6-f-4-color has-text-color">Contents:</h2>
<!-- /wp:heading -->
```

```wordpress-block
<!-- wp:list {"ordered":true,"textColor":"#f6f6f4"} -->
<ol class="wp-block-list has-f-6-f-6-f-4-color has-text-color"><!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1" target="_blank" rel="noreferrer noopener">Introduction</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2" target="_blank" rel="noreferrer noopener">A DPS Primer</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3" target="_blank" rel="noreferrer noopener">Creating DPS, IoT Hub and ADR Instances</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4" target="_blank" rel="noreferrer noopener">Understanding X.509 and CSR Workflows</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5" target="_blank" rel="noreferrer noopener">The Device Application</a> (This Post)</li>
<!-- /wp:list-item --></ol>
<!-- /wp:list -->
```

### Why This Sample Uses a Custom DPS Framework

```wordpress-block
<!-- wp:quote -->
<blockquote class="wp-block-quote"><!-- wp:paragraph -->
<p>This repository's sample uses a custom MQTT-based DPS framework for its CSR-based certificate issuance flow. Check the current Microsoft Learn documentation for service requirements and supported client workflows.</p>
<!-- /wp:paragraph -->

<!-- wp:paragraph {"className":"is-style-warning","fontSize":"medium"} -->
<p class="is-style-warning has-medium-font-size"><strong><em>Note: While these features are in preview, It's super important that you don't try to use the Azure Portal to remove your resources.<br><br>For now I've created a couple of scripts to help you remove your resources, you'll find them at the end of this post.</em></strong></p>
<!-- /wp:paragraph --></blockquote>
<!-- /wp:quote -->
```

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
          │  (Custom MQTT + CSR)    │
          └──────────┬──────────────┘
                     │
                     ▼
              ┌─────────────┐
              │   IoT Hub   │
              └─────────────┘

```

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
configure the onboarding credential and certificate policy, link DPS to the
namespace and new hub, sync the policy, and assign it to the enrollment. Keep
the resulting DPS ID Scope, hub hostname, and onboarding credential for the
application configuration.

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
    },
    ...
  }
  "IoTHub": {
    "DpsProvisioning": {
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "IdScope": "YOUR_DPS_ID_SCOPE",
      "RegistrationId": "YOUR_DEVICE_REGISTRATION_ID",
      "AttestationMethod": "SymmetricKey",
      "EnrollmentGroupKeyBase64": "YOUR_ENROLLMENT_GROUP_PRIMARY_KEY",
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
    "DeviceId": "my-device",
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
-   **IssuedCertPath**: Where the operational certificate from DPS will be saved
-   **Mock**: Enable hardware simulation for testing without physical devices

#### Step 4: Run the Application

**Build and run:**

```powershell
cd ..
dotnet run
```

**Expected output:**

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
✓ Certificate saved to: ./certs/issued/device.pfx

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

### Verify in the Portal

You should now be able to navigate to your resource group in the Azure Portal and see your device happily registered and showing as a first class citizen;

```wordpress-block
<!-- wp:image {"id":5091,"sizeSlug":"full","linkDestination":"media"} -->
<figure class="wp-block-image size-full"><a href="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-1.png"><img src="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-1.png" alt="" class="wp-image-5091"/></a></figure>
<!-- /wp:image -->
```

If you Navigate to your IoT Hub, and then to Device Management, and Devices, you should see your traditional IoT Hub Device present too;

```wordpress-block
<!-- wp:image {"id":5092,"sizeSlug":"large","linkDestination":"media"} -->
<figure class="wp-block-image size-large"><a href="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-2.png"><img src="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image-2-1024x192.png" alt="" class="wp-image-5092"/></a></figure>
<!-- /wp:image -->
```

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

```wordpress-block
<!-- wp:image {"id":5090,"sizeSlug":"full","linkDestination":"media"} -->
<figure class="wp-block-image size-full"><a href="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image.png"><img src="https://www.petecodes.co.uk/wp-content/uploads/2026/02/image.png" alt="" class="wp-image-5090"/></a></figure>
<!-- /wp:image -->
```

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

**Delete and re-provision:**

```powershell
Remove-Item "./certs/issued/device.pfx" -ErrorAction SilentlyContinue
dotnet run
```

**Problem: Missing `appsettings.json` configuration**

```plaintext
Unhandled exception. System.Exception: IoTHub config missing
```

**Solution:**

-   Ensure `appsettings.json` exists in project root
-   Copy from `appsettings.x509.json` template if needed:

```powershell
Copy-Item "appsettings.x509.json" "appsettings.json"
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

-   Delete expired certificate and re-provision
-   Operational certificates are short-lived (30 days default)
-   Application should auto-renew within 7-day window

**Force renewal:**

```powershell
Remove-Item "./certs/issued/device.pfx" -ErrorAction SilentlyContinue
dotnet run
```

**Still having issues?**

1.  Check Azure resources are properly configured ([Creating DPS, IoT Hub and ADR Instances](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/))
2.  Verify certificate and CSR setup ([Understanding X.509 and CSR Workflows](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/))
3.  Review the current [Microsoft Learn deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup) for enrollment and credential-policy configuration
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
├── AzureDpsFramework/                  # Custom DPS framework
│   ├── ProvisioningDeviceClient.cs
│   ├── DpsConfiguration.cs
│   ├── SecurityProvider*.cs
│   ├── Transport/
│   │   └── ProvisioningTransportHandlerMqtt.cs
│   └── Adr/
│       └── AdrDeviceRegistryClient.cs
└── appsettings.json                    # Device configuration
```

#### Main Program Flow (src/Program.cs)

The main program orchestrates the device lifecycle:

```csharp
static async Task Main(string[] args)
{
    // 1. Load configuration
    var iotConfig = ConfigurationLoader.LoadIoTHubConfiguration();
    var mockConfig = ConfigurationLoader.LoadMockConfiguration();
    
    // 2. Initialize hardware (mocked or real)
    using var colorSensor = mockConfig.EnableMockColorSensor 
        ? new TCS3472x(true, mockConfig.MockColorSequence)
        : new TCS3472x();
    
    var servo = new ServoController(mockConfig.EnableMockServos);
    
    // 3. Initialize DPS and connect to IoT Hub
    DeviceClient? deviceClient = DpsInitializationService.Initialize(iotConfig);
    
    // 4. Create telemetry service
    TelemetryService? telemetryService = null;
    if (deviceClient != null)
    {
        telemetryService = new TelemetryService(deviceClient, iotConfig.DeviceId);
    }
    
    // 5. Main sorting loop
    while (true)
    {
        // Pick, scan, sort, and report telemetry
        servo.MoveToPickPosition();
        var (clear, red, green, blue) = colorSensor.ReadColor();
        string color = colorSensor.ClassifySkittleColor(red, green, blue, clear);
        
        if (telemetryService != null)
        {
            await telemetryService.SendSkittleColorTelemetryAsync(color);
        }
        
        servo.MoveToColorChute(color);
    }
}
```

**Key responsibilities:**

1.  **Configuration loading** - Reads `appsettings.json` for DPS, IoT Hub, and hardware settings
2.  **Hardware initialization** - Sets up color sensor and servo controllers (real or mocked)
3.  **DPS provisioning** - Calls our custom framework to get operational certificate
4.  **IoT Hub connection** - Establishes MQTT connection using X.509 certificate
5.  **Business logic** - Runs the main sorting loop with telemetry reporting

#### DPS Initialization Service (src/comms/DpsInitializationService.cs)

This service handles the complete provisioning workflow:

**Phase 1: Check for existing certificate**

```csharp
if (File.Exists(dpsCfg.IssuedCertPath))
{
    Console.WriteLine("Device already provisioned. Using existing certificate.");
    return ConnectToIoTHub(dpsCfg, iotConfig);
}
```

**Phase 2: Provision device with bootstrap credentials**

```csharp
// Load bootstrap certificate
var bootstrapCert = new X509Certificate2(
    dpsCfg.AttestationCertPath, 
    dpsCfg.AttestationCertPassword
);

// Generate new key for operational certificate
RSA rsa = RSA.Create(2048);

// Create security provider
var security = new SecurityProviderX509CsrWithCert(
    dpsCfg.RegistrationId,
    bootstrapCert,
    rsa
);

// Create provisioning client
var client = ProvisioningDeviceClient.Create(
    dpsCfg.ProvisioningHost,
    dpsCfg.IdScope,
    security,
    new ProvisioningTransportHandlerMqtt()
);

// Register device
var result = client.RegisterAsync().GetAwaiter().GetResult();
```

**Phase 3: Save issued certificate**

```csharp
// Parse issued certificate from DPS response
byte[] certBytes = Convert.FromBase64String(result.IssuedCertificateChain[0]);
var certificate = new X509Certificate2(certBytes);

// Combine with private key
var certWithKey = certificate.CopyWithPrivateKey(rsa);

// Export to PFX for storage
byte[] pfx = certWithKey.Export(X509ContentType.Pfx, dpsCfg.IssuedCertPassword);
File.WriteAllBytes(dpsCfg.IssuedCertPath, pfx);
```

**Phase 4: Connect to IoT Hub**

```csharp
var certificate = new X509Certificate2(
    dpsCfg.IssuedCertPath, 
    dpsCfg.IssuedCertPassword
);

var auth = new DeviceAuthenticationWithX509Certificate(
    iotConfig.DeviceId, 
    certificate
);

var deviceClient = DeviceClient.Create(
    assignedHub, 
    auth, 
    TransportType.Mqtt
);
```

#### Custom DPS Framework (AzureDpsFramework/)

**Why I built it:**

This repository's custom DPS framework implements the `2025-07-01-preview`
CSR workflow used by the sample application. The service features demonstrated
include:

-   Certificate Signing Request (CSR) submission during registration
-   Receiving issued certificates in the registration response
-   Microsoft-managed PKI via Azure Device Registry

**What it does:**

My custom framework implements the preview API using raw MQTT communication:

**1\. Security Providers** (SecurityProvider\*.cs)

```csharp
// SecurityProviderX509CsrWithCert.cs
public class SecurityProviderX509CsrWithCert : SecurityProvider
{
    private readonly X509Certificate2 _bootstrapCertificate;
    private readonly RSA _operationalKey;
    
    public SecurityProviderX509CsrWithCert(
        string registrationId,
        X509Certificate2 bootstrapCert,
        RSA operationalKey)
    {
        _bootstrapCertificate = bootstrapCert;
        _operationalKey = operationalKey;
    }
    
    public string GenerateCsr()
    {
        // Create Certificate Signing Request
        var request = new CertificateRequest(
            $"CN={RegistrationId}",
            _operationalKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        
        return Convert.ToBase64String(request.CreateSigningRequest());
    }
}
```

This generates a CSR using the new operational key pair, not the bootstrap certificate's key.

**2\. MQTT Transport Handler** (ProvisioningTransportHandlerMqtt.cs)

```csharp
public async Task<DeviceRegistrationResult> RegisterAsync(
    string idScope,
    SecurityProvider security)
{
    // Connect to DPS with bootstrap credentials
    var mqttClient = await ConnectAsync(security);
    
    // Subscribe to response topics
    await mqttClient.SubscribeAsync("$dps/registrations/res/#");
    
    // Build registration payload with CSR
    var payload = new
    {
        registrationId = security.RegistrationId,
        certificateSigningRequest = security.GenerateCsr()
    };
    
    // Publish registration request
    await mqttClient.PublishAsync(
        $"$dps/registrations/PUT/iotdps-register/?$rid={operationId}&api-version=2025-07-01-preview",
        JsonSerializer.Serialize(payload)
    );
    
    // Wait for response
    var response = await WaitForResponseAsync(mqttClient, operationId);
    
    // Parse issued certificate from response
    return new DeviceRegistrationResult
    {
        AssignedHub = response.AssignedHub,
        DeviceId = response.DeviceId,
        IssuedCertificateChain = response.IssuedCertificateChain
    };
}
```

**Key features:**

-   Uses **preview API version** (`2025-07-01-preview`)
-   Includes **CSR in registration payload**
-   Extracts **issued certificate from response**
-   Handles **MQTT QoS and response correlation**

**3\. Provisioning Client** (ProvisioningDeviceClient.cs)

```csharp
public class ProvisioningDeviceClient
{
    public static ProvisioningDeviceClient Create(
        string provisioningHost,
        string idScope,
        SecurityProvider security,
        ProvisioningTransportHandler transport)
    {
        return new ProvisioningDeviceClient(
            provisioningHost, 
            idScope, 
            security, 
            transport
        );
    }
    
    public async Task<DeviceRegistrationResult> RegisterAsync()
    {
        // Delegate to transport handler
        return await _transport.RegisterAsync(_idScope, _security);
    }
}
```

#### Telemetry Service (src/comms/TelemetryService.cs)

Handles sending device telemetry to IoT Hub:

```csharp
public async Task SendSkittleColorTelemetryAsync(string color)
{
    var telemetry = new
    {
        deviceId = _deviceId,
        timestamp = DateTime.UtcNow,
        color = color,
        count = _colorCounts.GetValueOrDefault(color, 1)
    };
    
    string json = JsonSerializer.Serialize(telemetry);
    var message = new Message(Encoding.UTF8.GetBytes(json))
    {
        ContentType = "application/json",
        ContentEncoding = "utf-8"
    };
    
    message.Properties.Add("skittleColor", color);
    message.Properties.Add("deviceType", "skittle-sorter");
    
    await _deviceClient.SendEventAsync(message);
}
```

**Features:**

-   JSON-formatted telemetry messages
-   Custom message properties for routing
-   Per-color count tracking
-   Batch summary reporting

#### Certificate Renewal

The following example shows this repository's sample expiration check and
DPS-based refresh behavior. It isn't the complete current Azure
certificate-management renewal flow: current IoT Hub workflows support
operational-certificate renewal without requiring reprovisioning. Review the
[current certificate-management overview](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview)
before using or adapting this code.

```csharp
public static async Task CheckCertificateRenewal(DpsConfiguration dpsCfg)
{
    var cert = new X509Certificate2(
        dpsCfg.IssuedCertPath, 
        dpsCfg.IssuedCertPassword
    );
    
    // Check if within renewal window (7 days before expiry)
    var renewalWindow = cert.NotAfter.AddDays(-7);
    
    if (DateTime.UtcNow >= renewalWindow)
    {
        Console.WriteLine("Certificate renewal window reached. Re-provisioning...");
        
        // Delete old certificate
        File.Delete(dpsCfg.IssuedCertPath);
        
        // Trigger re-provisioning (same flow as initial)
    }
}
```

### Azure Device Registry (ADR) Integration

Azure Device Registry provides device identity and metadata management that works with DPS and IoT Hub.

#### Key ADR Features

**🔹 Device Metadata**

-   Attributes (hardware ID, model, version)
-   Tags (location, tenant, environment)
-   Custom properties

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
    Console.WriteLine($"  - {device.Name} ({device.Properties?.Enabled ?? false})");
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
    Console.WriteLine($"Enabled: {deviceDetails.Properties?.Enabled}");
    Console.WriteLine($"Hardware ID: {deviceDetails.Properties?.Attributes?.HardwareId}");
    
    // Display tags
    if (deviceDetails.Properties?.Tags != null)
    {
        Console.WriteLine("Tags:");
        foreach (var tag in deviceDetails.Properties.Tags)
        {
            Console.WriteLine($"  {tag.Key}: {tag.Value}");
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
        "HardwareId": "RPI4B-12345678",
        "Manufacturer": "Raspberry Pi Foundation",
        "Model": "Raspberry Pi 4B",
        "OperatingSystemVersion": "Raspbian GNU/Linux 11"
      },
      "Tags": {
        "location": "factory-1",
        "department": "manufacturing",
        "firmware": "1.0.5"
      }
    }
  }
}
```

The application automatically updates device metadata after provisioning.

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
az iot ops asset endpoint device list `
  --namespace "my-adrnamespace-uniquesuffix" `
  --resource-group "my-iot-rg" `
  --output table
```

**Expected output:**

```plaintext
Name        Enabled    HardwareId         Location
----------  ---------  -----------------  ----------
my-device   True       RPI4B-12345678     factory-1
```

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

### Removing your Resources and Clean Test Start Script

```wordpress-block
<!-- wp:paragraph {"className":"is-style-warning","fontSize":"medium"} -->
<p class="is-style-warning has-medium-font-size">Note: While these features are in preview, It's super important that you don't try to use the Portal to remove your resources.<br><br>For now I've created a couple of scripts to help you remove your resources.</p>
<!-- /wp:paragraph -->
```

#### **Remove Resources:**

To remove your resources, it's important that you remove them in teh right order, otherwise you may find that you have resources such as the ADR Namespace, which cannot be deleted.

You'll most certainly find this if you try to remove your resources using the portal, as it doesn't know yet to delete the resources in order.

This script will remove your resources safely.

```powershell
cd scripts
.\remove-resources.ps1 `
  -DpsName "<your-dps-name>" `
  -IotHubName "<your-iothub-name>" `
  -ResourceGroup "<your-resource-group>" `
  -EnrollmentGroupName "<your-enrollment-group-name>" `
  -DeviceId "<your-device-id>" `
  -AdrNamespace "<your-adr-namespace>" `
  -UserIdentity "<your-user-assigned-identity-name>" `
  -RootCertName "<your-root-cert-name>" `
  -IntermediateCertName "<your-intermediate-cert-name>"
```

**What it does:**

1.  Stops running processes
2.  Deletes issued certificates
3.  Removes device from IoT Hub
4.  Removes device from ADR
5.  Waits for propagation

#### **Run clean test:**

```powershell
cd scripts
.\clean-test-start.ps1 `
  -DpsName "<your-dps-name>" `
  -IotHubName "<your-iothub-name>" `
  -ResourceGroup "<your-resource-group>" `
  -EnrollmentGroupName "<your-enrollment-group-name>" `
  -DeviceId "<your-device-id>" `
  -AdrNamespace "<your-adr-namespace>" `
  -RootCertName "<your-root-cert-name>" `
  -IntermediateCertName "<your-intermediate-cert-name>" `
  -RemoveCaCerts
```

**What it does:**

1.  Stops running processes
2.  Deletes issued certificates
3.  Removes device from IoT Hub
4.  Removes device from ADR
5.  Waits for propagation
6.  Runs application fresh

This ensures clean provisioning from scratch every time.

### Key Takeaways

✅ **Custom DPS Framework** - This sample uses a custom MQTT implementation
✅ **Two-Phase Authentication** - Bootstrap cert for DPS, operational cert for IoT Hub  
✅ **CSR-Based Issuance** - Private key never leaves device  
✅ **MQTT Protocol** - Direct protocol implementation for preview features  
✅ **Certificate Lifecycle** - Automatic storage, loading, and renewal logic  
✅ **Mock Hardware Support** - Test without physical devices

### What We Accomplished

✅  Cloned the repository and ran full setup automation  
✅ Configured the device application with Azure resources  
✅ Understood the main program flow and component architecture  
✅ Explored the custom DPS framework used by this sample
✅ Learned how CSR-based provisioning works end-to-end  
✅ Integrated with Azure Device Registry for metadata management  
✅ Tested the complete solution with telemetry monitoring  
✅ Reviewed the documented preview limitations and production warning

### Further Learning

-   **Azure IoT Hub Documentation**: [learn.microsoft.com/azure/iot-hub](https://learn.microsoft.com/azure/iot-hub/)
-   **Device Provisioning Service**: [learn.microsoft.com/azure/iot-dps](https://learn.microsoft.com/azure/iot-dps/)
-   **Azure Device Registry**: [learn.microsoft.com/azure/iot/iot-device-registry-overview](https://learn.microsoft.com/azure/iot/iot-device-registry-overview)
-   **X.509 Certificates**: [learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview](https://learn.microsoft.com/azure/iot-hub/iot-hub-x509ca-overview)

### Questions or Issues?

-   Open an issue on GitHub
-   Check the [Troubleshooting.md guide](https://github.com/pjgpetecodes/skittlesorter/blob/main/docs/Troubleshooting.md)
-   Review the [current Azure setup guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)

Thank you for following this series! Happy IoT building! 🚀

```wordpress-block
<!-- wp:paragraph -->
<p></p>
<!-- /wp:paragraph -->
```
