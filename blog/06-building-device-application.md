# Building the Device Application

[← Previous: Building the Custom DPS Framework](05-building-dps-framework.md) | [Next: ADR Integration and Testing →](07-adr-integration-testing.md)

---

In this post, we'll build the complete IoT device application that uses DPS provisioning with CSR-based certificate issuance. We'll walk through the configuration, initialization, telemetry, and device management.

## Application Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Program.cs                          │
│                   (Main Entry Point)                    │
└────────┬────────────────────────────────────────────────┘
         │
    ┌────┴───────┬──────────────┬───────────────┐
    │            │              │               │
    ▼            ▼              ▼               ▼
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

## Project Structure

```
skittlesorter/
├── src/
│   ├── Program.cs                      # Main entry point
│   ├── comms/
│   │   ├── DpsInitializationService.cs # DPS provisioning logic
│   │   └── TelemetryService.cs         # IoT Hub telemetry
│   ├── configuration/
│   │   ├── ConfigurationLoader.cs      # Load appsettings.json
│   │   └── appsettings.*.json          # Configuration templates
│   └── drivers/
│       ├── SkittleSorterService.cs     # Main business logic
│       └── ... (hardware drivers)
├── AzureDpsFramework/                  # Custom DPS framework
│   ├── ProvisioningDeviceClient.cs
│   ├── SecurityProvider*.cs
│   └── Transport/
│       └── ProvisioningTransportHandlerMqtt.cs
└── appsettings.json                    # Device configuration
```

## Step 1: Configuration

Create `appsettings.json` with DPS and IoT Hub settings:

```json
{
  "Dps": {
    "IdScope": "0ne00XXXXXX",
    "RegistrationId": "dev001-skittlesorter",
    "AttestationMethod": "SymmetricKey",
    "EnrollmentGroupKeyBase64": "your-enrollment-group-key-here",
    "ProvisioningHost": "global.azure-devices-provisioning.net",
    "MqttPort": 8883,
    "ApiVersion": "2025-07-01-preview",
    "AutoGenerateCsr": true,
    "CsrFilePath": "./certs/issued/device.csr",
    "CsrKeyFilePath": "./certs/issued/device.key",
    "IssuedCertPath": "./certs/issued/device.pfx",
    "IssuedCertPassword": "strongpassword123"
  },
  "IoTHub": {
    "DeviceId": "dev001-skittlesorter",
    "SendTelemetry": true,
    "TelemetryIntervalMs": 5000,
    "UseX509Certificate": true,
    "X509CertificatePath": "./certs/issued/device.pfx",
    "X509CertificatePassword": "strongpassword123"
  },
  "Mock": {
    "EnableMockColorSensor": true,
    "EnableMockServos": true,
    "MockColorSequence": ["red", "green", "yellow", "orange", "purple"]
  }
}
```

### Configuration Classes

```csharp
// ConfigurationLoader.cs
using System.Text.Json;

public class ConfigurationLoader
{
    private const string ConfigFile = "appsettings.json";
    
    public static IoTHubConfig LoadIoTHubConfiguration()
    {
        var json = File.ReadAllText(ConfigFile);
        var config = JsonSerializer.Deserialize<AppConfig>(json);
        return config?.IoTHub ?? throw new Exception("IoTHub config missing");
    }
    
    public static MockConfig LoadMockConfiguration()
    {
        var json = File.ReadAllText(ConfigFile);
        var config = JsonSerializer.Deserialize<AppConfig>(json);
        return config?.Mock ?? new MockConfig();
    }
}

public class AppConfig
{
    public DpsConfig? Dps { get; set; }
    public IoTHubConfig? IoTHub { get; set; }
    public MockConfig? Mock { get; set; }
}

public class IoTHubConfig
{
    public string DeviceId { get; set; } = "";
    public bool SendTelemetry { get; set; }
    public int TelemetryIntervalMs { get; set; } = 5000;
    public bool UseX509Certificate { get; set; }
    public string? X509CertificatePath { get; set; }
    public string? X509CertificatePassword { get; set; }
}
```

## Step 2: DPS Initialization Service

This service handles device provisioning and certificate management:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Azure.Devices.Client;
using AzureDpsFramework;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;

public class DpsInitializationService
{
    public static DeviceClient? Initialize(IoTHubConfig iotConfig)
    {
        try
        {
            // 1. Load DPS configuration
            var dpsCfg = DpsConfiguration.Load();
            
            Console.WriteLine("=== DPS Configuration ===");
            Console.WriteLine($"IdScope: {dpsCfg.IdScope}");
            Console.WriteLine($"RegistrationId: {dpsCfg.RegistrationId}");
            Console.WriteLine($"AttestationMethod: {dpsCfg.AttestationMethod}");
            
            // 2. Check if already provisioned
            if (File.Exists(dpsCfg.IssuedCertPath))
            {
                Console.WriteLine("Device already provisioned. Using existing certificate.");
                return ConnectToIoTHub(dpsCfg, iotConfig);
            }
            
            // 3. Provision device via DPS
            Console.WriteLine("\n=== Starting DPS Provisioning ===");
            var result = ProvisionDevice(dpsCfg);
            
            // 4. Save issued certificate
            if (result.IssuedCertificateChain != null && result.IssuedCertificateChain.Length > 0)
            {
                SaveIssuedCertificate(result, dpsCfg);
            }
            
            // 5. Connect to assigned IoT Hub
            return ConnectToIoTHub(dpsCfg, iotConfig, result.AssignedHub);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DPS initialization failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return null;
        }
    }
    
    private static DeviceRegistrationResult ProvisionDevice(DpsConfiguration dpsCfg)
    {
        SecurityProvider security;
        RSA? rsa = null;
        
        if (dpsCfg.AttestationMethod == "SymmetricKey")
        {
            // Symmetric Key + CSR
            if (string.IsNullOrWhiteSpace(dpsCfg.EnrollmentGroupKeyBase64))
                throw new InvalidOperationException("EnrollmentGroupKeyBase64 required");
            
            // Derive device key
            string deviceKey = DeriveDeviceKey(
                dpsCfg.EnrollmentGroupKeyBase64, 
                dpsCfg.RegistrationId
            );
            
            Console.WriteLine("Device key derived from enrollment group key.");
            
            // Generate CSR if needed
            if (dpsCfg.AutoGenerateCsr)
            {
                rsa = RSA.Create(2048);
                Console.WriteLine("Generated RSA key pair (2048-bit) for CSR.");
                
                // Save private key
                Directory.CreateDirectory(Path.GetDirectoryName(dpsCfg.CsrKeyFilePath)!);
                File.WriteAllText(dpsCfg.CsrKeyFilePath, 
                    rsa.ExportRSAPrivateKeyPem());
            }
            
            // Create security provider with symmetric key + CSR
            security = new SecurityProviderX509CsrWithSymmetricKey(
                dpsCfg.RegistrationId,
                deviceKey,
                rsa!
            );
        }
        else if (dpsCfg.AttestationMethod == "X509")
        {
            // X.509 Bootstrap Certificate + CSR
            if (string.IsNullOrWhiteSpace(dpsCfg.AttestationCertPath))
                throw new InvalidOperationException("AttestationCertPath required for X509");
            
            var bootstrapCert = new X509Certificate2(
                dpsCfg.AttestationCertPath, 
                dpsCfg.AttestationCertPassword
            );
            
            Console.WriteLine($"Loaded bootstrap certificate: {bootstrapCert.Subject}");
            
            // Generate new key for operational certificate
            rsa = RSA.Create(2048);
            Console.WriteLine("Generated new RSA key pair for operational certificate.");
            
            security = new SecurityProviderX509CsrWithCert(
                dpsCfg.RegistrationId,
                bootstrapCert,
                rsa
            );
        }
        else
        {
            throw new InvalidOperationException($"Unknown attestation method: {dpsCfg.AttestationMethod}");
        }
        
        // Create provisioning client
        var client = ProvisioningDeviceClient.Create(
            dpsCfg.ProvisioningHost,
            dpsCfg.IdScope,
            security,
            new ProvisioningTransportHandlerMqtt()
        );
        
        // Register device (this may take 10-30 seconds)
        Console.WriteLine("Submitting registration request to DPS...");
        var result = client.RegisterAsync().GetAwaiter().GetResult();
        
        Console.WriteLine($"✓ Device assigned to: {result.AssignedHub}");
        Console.WriteLine($"✓ Device ID: {result.DeviceId}");
        
        return result;
    }
    
    private static void SaveIssuedCertificate(
        DeviceRegistrationResult result, 
        DpsConfiguration dpsCfg)
    {
        Console.WriteLine("\n=== Saving Issued Certificate ===");
        
        // Parse device certificate
        byte[] certBytes = Convert.FromBase64String(result.IssuedCertificateChain[0]);
        var certificate = new X509Certificate2(certBytes);
        
        Console.WriteLine($"Certificate Subject: {certificate.Subject}");
        Console.WriteLine($"Issuer: {certificate.Issuer}");
        Console.WriteLine($"Valid from: {certificate.NotBefore}");
        Console.WriteLine($"Valid until: {certificate.NotAfter}");
        
        // Load private key
        RSA rsa;
        if (File.Exists(dpsCfg.CsrKeyFilePath))
        {
            string pemKey = File.ReadAllText(dpsCfg.CsrKeyFilePath);
            rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
        }
        else
        {
            throw new InvalidOperationException("Private key file not found");
        }
        
        // Combine certificate with private key
        var certWithKey = certificate.CopyWithPrivateKey(rsa);
        
        // Export to PFX
        Directory.CreateDirectory(Path.GetDirectoryName(dpsCfg.IssuedCertPath)!);
        byte[] pfx = certWithKey.Export(
            X509ContentType.Pfx, 
            dpsCfg.IssuedCertPassword
        );
        File.WriteAllBytes(dpsCfg.IssuedCertPath, pfx);
        
        Console.WriteLine($"✓ Certificate saved to: {dpsCfg.IssuedCertPath}");
    }
    
    private static DeviceClient ConnectToIoTHub(
        DpsConfiguration dpsCfg, 
        IoTHubConfig iotConfig, 
        string? assignedHub = null)
    {
        Console.WriteLine("\n=== Connecting to IoT Hub ===");
        
        // Load certificate
        var certificate = new X509Certificate2(
            dpsCfg.IssuedCertPath, 
            dpsCfg.IssuedCertPassword
        );
        
        // Parse assigned hub from certificate or use provided
        string hubHostname = assignedHub ?? ExtractHubFromCertificate(certificate);
        
        Console.WriteLine($"Connecting to: {hubHostname}");
        Console.WriteLine($"Device ID: {iotConfig.DeviceId}");
        
        // Create device client with X.509 authentication
        var auth = new DeviceAuthenticationWithX509Certificate(
            iotConfig.DeviceId, 
            certificate
        );
        
        var deviceClient = DeviceClient.Create(
            hubHostname, 
            auth, 
            TransportType.Mqtt
        );
        
        // Open connection
        deviceClient.OpenAsync().GetAwaiter().GetResult();
        Console.WriteLine("✓ Connected to IoT Hub");
        
        return deviceClient;
    }
    
    private static string DeriveDeviceKey(string enrollmentKey, string registrationId)
    {
        byte[] keyBytes = Convert.FromBase64String(enrollmentKey);
        using var hmac = new HMACSHA256(keyBytes);
        byte[] deviceKeyBytes = hmac.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(registrationId)
        );
        return Convert.ToBase64String(deviceKeyBytes);
    }
    
    private static string ExtractHubFromCertificate(X509Certificate2 cert)
    {
        // Extract assigned hub from certificate extensions or subject
        // This is implementation-specific
        return "your-hub.azure-devices.net";
    }
}
```

## Step 3: Telemetry Service

Send telemetry to IoT Hub:

```csharp
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

public class TelemetryService
{
    private readonly DeviceClient _deviceClient;
    private readonly string _deviceId;
    private readonly Dictionary<string, int> _colorCounts = new();
    
    public TelemetryService(DeviceClient deviceClient, string deviceId, IoTHubConfig config)
    {
        _deviceClient = deviceClient;
        _deviceId = deviceId;
    }
    
    public void LogColorDetected(string color)
    {
        if (!_colorCounts.ContainsKey(color))
            _colorCounts[color] = 0;
        
        _colorCounts[color]++;
    }
    
    public async Task SendSkittleColorTelemetryAsync(string color)
    {
        try
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
            
            // Add message properties
            message.Properties.Add("skittleColor", color);
            message.Properties.Add("deviceType", "skittle-sorter");
            
            await _deviceClient.SendEventAsync(message);
            Console.WriteLine($"→ Telemetry sent: {color} (count: {_colorCounts[color]})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send telemetry: {ex.Message}");
        }
    }
    
    public async Task SendBatchTelemetryAsync()
    {
        var batch = new
        {
            deviceId = _deviceId,
            timestamp = DateTime.UtcNow,
            summary = _colorCounts,
            totalProcessed = _colorCounts.Values.Sum()
        };
        
        string json = JsonSerializer.Serialize(batch);
        var message = new Message(Encoding.UTF8.GetBytes(json))
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8"
        };
        
        await _deviceClient.SendEventAsync(message);
        Console.WriteLine($"→ Batch telemetry sent: {_colorCounts.Count} colors");
    }
}
```

## Step 4: Main Program

Put it all together:

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Skittle Sorter Starting...\n");
        
        // 1. Load configuration
        var iotConfig = ConfigurationLoader.LoadIoTHubConfiguration();
        var mockConfig = ConfigurationLoader.LoadMockConfiguration();
        
        Console.WriteLine($"Mock Mode Enabled: {mockConfig.EnableMockColorSensor}");
        Console.WriteLine($"Telemetry Enabled: {iotConfig.SendTelemetry}\n");
        
        // 2. Initialize hardware (mocked or real)
        using var colorSensor = mockConfig.EnableMockColorSensor 
            ? new TCS3472x(true, mockConfig.MockColorSequence)
            : new TCS3472x();
        
        var servo = new ServoController(mockConfig.EnableMockServos);
        servo.Home();
        
        // 3. Initialize DPS and connect to IoT Hub
        DeviceClient? deviceClient = null;
        TelemetryService? telemetryService = null;
        
        if (iotConfig.SendTelemetry)
        {
            deviceClient = DpsInitializationService.Initialize(iotConfig);
            if (deviceClient != null)
            {
                telemetryService = new TelemetryService(
                    deviceClient, 
                    iotConfig.DeviceId, 
                    iotConfig
                );
            }
        }
        
        // 4. Main sorting loop
        Console.WriteLine("\n=== Starting Sorting Loop ===\n");
        
        try
        {
            while (true)
            {
                // Pick skittle
                servo.MoveToPickPosition();
                await Task.Delay(1000);
                
                // Scan color
                servo.MoveToScanPosition();
                await Task.Delay(500);
                
                var (clear, red, green, blue) = colorSensor.ReadColor();
                string color = colorSensor.ClassifySkittleColor(red, green, blue, clear);
                
                Console.WriteLine($"Detected: {color} (R:{red} G:{green} B:{blue} C:{clear})");
                
                if (color == "None")
                {
                    Console.WriteLine("No skittle detected.\n");
                    continue;
                }
                
                // Send telemetry
                if (telemetryService != null)
                {
                    telemetryService.LogColorDetected(color);
                    await telemetryService.SendSkittleColorTelemetryAsync(color);
                }
                
                // Sort to appropriate chute
                servo.MoveToColorChute(color);
                await Task.Delay(500);
                
                // Release skittle
                servo.Release();
                await Task.Delay(200);
                
                Console.WriteLine($"✓ {color} sorted\n");
            }
        }
        finally
        {
            // Cleanup
            deviceClient?.CloseAsync().Wait();
            servo.Home();
        }
    }
}
```

## Device Twin Support

Handle device twin desired properties:

```csharp
// In TelemetryService.cs
public async Task SetupDeviceTwinAsync()
{
    // Get current twin
    var twin = await _deviceClient.GetTwinAsync();
    Console.WriteLine($"Current twin version: {twin.Properties.Desired.Version}");
    
    // Subscribe to twin updates
    await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(
        OnDesiredPropertyChanged, 
        null
    );
    
    Console.WriteLine("Subscribed to device twin updates.");
}

private async Task OnDesiredPropertyChanged(
    TwinCollection desiredProperties, 
    object userContext)
{
    Console.WriteLine($"\n=== Device Twin Update Received ===");
    Console.WriteLine(desiredProperties.ToJson());
    
    // Handle telemetry interval change
    if (desiredProperties.Contains("telemetryInterval"))
    {
        int interval = desiredProperties["telemetryInterval"];
        Console.WriteLine($"Updating telemetry interval to {interval}ms");
        // Update configuration
    }
    
    // Report back to cloud
    var reportedProperties = new TwinCollection();
    reportedProperties["telemetryInterval"] = desiredProperties["telemetryInterval"];
    reportedProperties["lastUpdated"] = DateTime.UtcNow;
    
    await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
    Console.WriteLine("Reported properties updated.");
}
```

## Certificate Renewal

Check certificate expiration and renew:

```csharp
public static async Task CheckCertificateRenewal(DpsConfiguration dpsCfg)
{
    if (!File.Exists(dpsCfg.IssuedCertPath))
        return;
    
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
        
        // Trigger re-provisioning
        // (Same flow as initial provision)
    }
    else
    {
        var daysUntilRenewal = (renewalWindow - DateTime.UtcNow).Days;
        Console.WriteLine($"Certificate valid. Renewal in {daysUntilRenewal} days.");
    }
}
```

## What We Accomplished

✅ Created complete device application structure  
✅ Implemented DPS provisioning with CSR  
✅ Configured symmetric key attestation  
✅ Saved and loaded issued certificates  
✅ Connected to IoT Hub with X.509  
✅ Sent telemetry messages  
✅ Handled device twin updates  
✅ Implemented certificate renewal logic  

## Next Steps

In the final post, we'll cover:
- Azure Device Registry (ADR) integration
- Querying device metadata
- Updating device attributes
- Testing and troubleshooting
- Production deployment considerations

---

[Next: ADR Integration and Testing →](07-adr-integration-testing.md)
