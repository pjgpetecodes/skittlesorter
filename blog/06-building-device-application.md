# Building the Device Application

[← Previous: Building the Custom DPS Framework](05-building-dps-framework.md) | [Back to Introduction](00-introduction.md)

---

In this post, we'll walk through the complete IoT device application that uses DPS provisioning with CSR-based certificate issuance. Rather than building from scratch, we'll clone the repository, run the setup automation, configure the application, and explore how the major components work together.

> **Why a Custom DPS Framework?** Microsoft's official C# SDK doesn't yet support the `2025-07-01-preview` API required for CSR-based certificate issuance. I've built a custom MQTT-based DPS framework that handles the preview API features.

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
          │  Azure DPS Framework    │
          │  (Custom MQTT + CSR)    │
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
git clone https://github.com/your-username/skittlesorter.git
cd skittlesorter
```

## Step 2: Run Full Azure Setup (If Not Already Done)

If you haven't already run the Azure setup automation from [Post 02](02-creating-azure-resources.md), run the comprehensive setup script to create all Azure resources, certificates, and enrollment groups:

**Set your parameters:**

```powershell
$resourceGroup = "my-iot-rg"
$location = "eastus"
$iotHubName = "my-iothub-001"
$dpsName = "my-dps-001"
$adrNamespace = "my-adrnamespace-001"
$userIdentity = "my-uami"
$registrationId = "my-device"
```

**Run the full setup script:**

```powershell
cd scripts

.\setup-x509-dps-adr.ps1 `
  -ResourceGroup $resourceGroup `
  -Location $location `
  -IoTHubName $iotHubName `
  -DPSName $dpsName `
  -AdrNamespace $adrNamespace `
  -UserIdentity $userIdentity `
  -RegistrationId $registrationId
```

This script will:
- Create resource group, IoT Hub, DPS, and ADR namespace
- Generate root CA, intermediate CA, and device bootstrap certificates
- Upload and verify certificates with DPS
- Create enrollment group with credential policy
- Link all services together

**Save the output values:**
- **IdScope**: From DPS (format: `0ne00XXXXXX`)
- **Hub Hostname**: Your IoT Hub endpoint
- **Certificate Paths**: Location of device certificates

## Step 3: Configure the Application

Update `appsettings.json` with your values from the setup script:

```json
{
  "IoTHub": {
    "DpsProvisioning": {
      "IdScope": "0ne00XXXXXX",
      "RegistrationId": "my-device",
      "AttestationMethod": "X509",
      "AttestationCertPath": "./scripts/certs/device/device.pem",
      "AttestationKeyPath": "./scripts/certs/device/device.key",
      "AttestationCertChainPath": "./scripts/certs/device/device-full-chain.pem",
      "ProvisioningHost": "global.azure-devices-provisioning.net",
      "MqttPort": 8883,
      "ApiVersion": "2025-07-01-preview",
      "AutoGenerateCsr": true,
      "CsrFilePath": "./certs/issued/device.csr",
      "CsrKeyFilePath": "./certs/issued/device.key",
      "IssuedCertPath": "./certs/issued/device.pfx",
      "IssuedCertPassword": "your-strong-password"
    },
    "DeviceId": "my-device",
    "SendTelemetry": true,
    "TelemetryIntervalMs": 5000
  },
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ResourceGroupName": "my-iot-rg",
    "NamespaceName": "my-adrnamespace-001"
  },
  "Mock": {
    "EnableMockColorSensor": true,
    "EnableMockServos": true,
    "MockColorSequence": ["red", "green", "yellow", "orange", "purple"]
  }
}
```

**Key Configuration Sections:**

- **DpsProvisioning**: Bootstrap certificate paths and DPS connection details
- **DeviceId**: Must match your registration ID
- **IssuedCertPath**: Where the operational certificate from DPS will be saved
- **Mock**: Enable hardware simulation for testing without physical devices

## Step 4: Run the Application

**Build and run:**

```powershell
cd ..
dotnet run
```

**Expected output:**

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

1. **DPS Configuration** - Loads settings from `appsettings.json`
2. **DPS Provisioning** - Authenticates with bootstrap certificate and generates CSR
3. **Saving Certificate** - Receives operational certificate from DPS and saves it locally
4. **Connecting to IoT Hub** - Uses operational certificate to establish MQTT connection
5. **Sorting Loop** - Processes skittles and sends telemetry

### Troubleshooting Common Issues

**Problem: "Failed to load bootstrap certificate"**

```
Error: The system cannot find the file specified.
at System.Security.Cryptography.X509Certificates.X509Certificate2..ctor
```

**Solution:**
- Verify `AttestationCertPath` in `appsettings.json` points to correct location
- Check if you ran the setup script from the `scripts/` directory
- Ensure certificates exist: `./scripts/certs/device/device.pem`

**Check certificate files:**

```powershell
Test-Path "./scripts/certs/device/device.pem"
Test-Path "./scripts/certs/device/device.key"
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
- Check that DPS is linked to IoT Hub
- Verify IoT Hub hostname matches assigned hub
- Ensure operational certificate was saved correctly

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
# Delete issued certificate
Remove-Item "./certs/issued/device.pfx" -ErrorAction SilentlyContinue

# Run application again (it will re-provision)
dotnet run
```

**Problem: Missing `appsettings.json` configuration**

```
Unhandled exception. System.Exception: IoTHub config missing
```

**Solution:**
- Ensure `appsettings.json` exists in project root
- Copy from `appsettings.x509.json` template if needed:

```powershell
Copy-Item "appsettings.x509.json" "appsettings.json"
```

**Edit with your values:**

```powershell
notepad appsettings.json
```

**Problem: Certificate expired or renewal needed**

```
Certificate Valid until: 2026-01-15 10:00:00Z (expired)
```

**Solution:**
- Delete expired certificate and re-provision
- Operational certificates are short-lived (30 days default)
- Application should auto-renew within 7-day window

**Force renewal:**

```powershell
Remove-Item "./certs/issued/device.pfx"
dotnet run
```

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

### Main Program Flow ([src/Program.cs](c:/repos/skittlesorter/src/Program.cs))

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
1. **Configuration loading** - Reads `appsettings.json` for DPS, IoT Hub, and hardware settings
2. **Hardware initialization** - Sets up color sensor and servo controllers (real or mocked)
3. **DPS provisioning** - Calls our custom framework to get operational certificate
4. **IoT Hub connection** - Establishes MQTT connection using X.509 certificate
5. **Business logic** - Runs the main sorting loop with telemetry reporting

### DPS Initialization Service ([src/comms/DpsInitializationService.cs](c:/repos/skittlesorter/src/comms/DpsInitializationService.cs))

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

### Custom DPS Framework ([AzureDpsFramework/](c:/repos/skittlesorter/AzureDpsFramework/))

**Why we built it:**

Microsoft's official C# Device Provisioning SDK doesn't support the `2025-07-01-preview` API version required for CSR-based certificate issuance. The preview features include:

- Certificate Signing Request (CSR) submission during registration
- Receiving issued certificates in the registration response
- Microsoft-managed PKI via Azure Device Registry

**What it does:**

Our custom framework implements the preview API using raw MQTT communication:

**1. Security Providers** ([SecurityProvider*.cs](c:/repos/skittlesorter/AzureDpsFramework/Security/))

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

**2. MQTT Transport Handler** ([ProvisioningTransportHandlerMqtt.cs](c:/repos/skittlesorter/AzureDpsFramework/Transport/ProvisioningTransportHandlerMqtt.cs))

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
- Uses **preview API version** (`2025-07-01-preview`)
- Includes **CSR in registration payload**
- Extracts **issued certificate from response**
- Handles **MQTT QoS and response correlation**

**3. Provisioning Client** ([ProvisioningDeviceClient.cs](c:/repos/skittlesorter/AzureDpsFramework/ProvisioningDeviceClient.cs))

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

### Telemetry Service ([src/comms/TelemetryService.cs](c:/repos/skittlesorter/src/comms/TelemetryService.cs))

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
- JSON-formatted telemetry messages
- Custom message properties for routing
- Per-color count tracking
- Batch summary reporting

### Certificate Renewal

The application monitors certificate expiration and triggers renewal:

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

## Azure Device Registry (ADR) Integration

Azure Device Registry provides device identity and metadata management that works with DPS and IoT Hub.

### Key ADR Features

**🔹 Device Metadata**
- Attributes (hardware ID, model, version)
- Tags (location, tenant, environment)
- Custom properties

**🔹 Credential Management**
- Credential policies for certificate issuance
- Automated lifecycle management
- Certificate rotation

**🔹 Multi-Hub Support**
- Device identity independent of IoT Hub
- Move devices between hubs without re-provisioning
- Query devices across multiple hubs

### ADR Client Implementation

The project includes an ADR client for device management ([AzureDpsFramework/Adr/AdrDeviceRegistryClient.cs](c:/repos/skittlesorter/AzureDpsFramework/Adr/AdrDeviceRegistryClient.cs)):

```csharp
using Azure.Identity;
using AzureDpsFramework.Adr;

// Create ADR client (uses DefaultAzureCredential)
var adrClient = new AdrDeviceRegistryClient();

// List all devices in namespace
var devices = await adrClient.ListDevicesAsync(
    subscriptionId: "your-subscription-id",
    resourceGroupName: "my-iot-rg",
    namespaceName: "my-adrnamespace-001"
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
    namespaceName: "my-adrnamespace-001",
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

### Configuration-Driven ADR Updates

Enable automatic device metadata updates in `appsettings.json`:

```json
{
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "your-subscription-id",
    "ResourceGroupName": "my-iot-rg",
    "NamespaceName": "my-adrnamespace-001",
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

## Testing the Complete Solution

### Test 1: Verify Device in IoT Hub

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
  --namespace "my-adrnamespace-001" `
  --resource-group "my-iot-rg" `
  --output table
```

**Expected output:**

```
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

## Clean Test Start Script

For testing, use the provided script to reset device state:

**Run clean test:**

```powershell
cd scripts
.\clean-test-start.ps1
```

**What it does:**

1. Stops running processes
2. Deletes issued certificates
3. Removes device from IoT Hub
4. Removes device from ADR
5. Waits for propagation
6. Runs application fresh

This ensures clean provisioning from scratch every time.

## Key Takeaways

✅ **Custom DPS Framework Required** - Official SDK doesn't support preview API  
✅ **Two-Phase Authentication** - Bootstrap cert for DPS, operational cert for IoT Hub  
✅ **CSR-Based Issuance** - Private key never leaves device  
✅ **MQTT Protocol** - Direct protocol implementation for preview features  
✅ **Certificate Lifecycle** - Automatic storage, loading, and renewal logic  
✅ **ADR Integration** - Device metadata management and querying  
✅ **Production Ready** - Complete testing, monitoring, and deployment guidance  

## What We Accomplished

✅ Cloned the repository and ran full setup automation  
✅ Configured the device application with Azure resources  
✅ Understood the main program flow and component architecture  
✅ Explored the custom DPS framework and why it's needed  
✅ Learned how CSR-based provisioning works end-to-end  
✅ Integrated with Azure Device Registry for metadata management  
✅ Tested the complete solution with telemetry monitoring  
✅ Prepared for production deployment  

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
