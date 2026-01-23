# Setting up a Simulated IoT Device (C#)

[← Previous: Creating Enrollment Groups](06-creating-enrollment-groups.md) | [Next: Setting up a Simulated Device (Python) →](07b-setting-up-python-device.md)

---

## Device-Side Implementation with C#

Up until now, all configuration has been cloud-side setup. Now we implement the device code that will load its certificate and automatically provision itself through DPS using C# and .NET.

## Overview

Now that DPS is configured with verified certificates and an enrollment group, we can set up a device that will automatically provision and connect to IoT Hub using X.509 authentication.

## What We're Building

A simulated device that:
1. Loads its X.509 certificate and private key
2. Connects to DPS using certificate authentication
3. Automatically gets assigned to an IoT Hub
4. Connects to IoT Hub using the same certificate
5. Sends telemetry data

**No connection strings required!**

## Prerequisites

- Device certificate from section 3 (`device.pem`, `device.key`)
- DPS ID Scope (from section 2)
- Registration ID (should match device certificate CN)
- .NET 6.0+ installed

## C# (.NET) Implementation

### Install Required Packages

First we create a new .NET console application and install the necessary Azure IoT SDK packages.

```bash
dotnet new console -n IoTDeviceSimulator
cd IoTDeviceSimulator

# Install Azure IoT SDKs
dotnet add package Microsoft.Azure.Devices.Provisioning.Client
dotnet add package Microsoft.Azure.Devices.Provisioning.Transport.Mqtt
dotnet add package Microsoft.Azure.Devices.Client
```

### Configuration File

Next we create a configuration file with our DPS details and certificate paths.

Create `appsettings.json`:

```json
{
  "DPS": {
    "ProvisioningHost": "global.azure-devices-provisioning.net",
    "IdScope": "0ne00123ABC",
    "RegistrationId": "my-device-001",
    "CertificatePath": "../certs/device/device.pem",
    "CertificateKeyPath": "../certs/device/device.key"
  }
}
```

**Important:** 
- `RegistrationId` must match the CN in your device certificate
- Update `IdScope` with your actual DPS ID scope

### Device Code

Now we implement the device code that loads certificates, provisions through DPS, and sends telemetry.

Create `Program.cs`:

```csharp
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== IoT Device Simulator with X.509 ===\n");

        // Load configuration
        var config = JsonSerializer.Deserialize<Config>(
            File.ReadAllText("appsettings.json"))!;

        // Load certificate
        var cert = LoadCertificate(
            config.DPS.CertificatePath,
            config.DPS.CertificateKeyPath);

        Console.WriteLine($"Certificate loaded: {cert.Subject}");
        Console.WriteLine($"Valid: {cert.NotBefore} to {cert.NotAfter}\n");

        // Provision through DPS
        var deviceClient = await ProvisionDevice(
            config.DPS.ProvisioningHost,
            config.DPS.IdScope,
            config.DPS.RegistrationId,
            cert);

        // Send telemetry
        await SendTelemetry(deviceClient);
    }

    static X509Certificate2 LoadCertificate(string certPath, string keyPath)
    {
        var certPem = File.ReadAllText(certPath);
        var keyPem = File.ReadAllText(keyPath);

        // Combine cert and key into X509Certificate2
        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    static async Task<DeviceClient> ProvisionDevice(
        string provisioningHost,
        string idScope,
        string registrationId,
        X509Certificate2 certificate)
    {
        Console.WriteLine("=== Starting DPS Provisioning ===");
        Console.WriteLine($"Host: {provisioningHost}");
        Console.WriteLine($"ID Scope: {idScope}");
        Console.WriteLine($"Registration ID: {registrationId}\n");

        // Create security provider with X.509 certificate
        var security = new SecurityProviderX509Certificate(certificate);

        // Create provisioning transport (MQTT)
        var transport = new ProvisioningTransportHandlerMqtt();

        // Create provisioning client
        var provisioningClient = ProvisioningDeviceClient.Create(
            provisioningHost,
            idScope,
            security,
            transport);

        // Register with DPS
        Console.WriteLine("Registering with DPS...");
        var result = await provisioningClient.RegisterAsync();

        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"Assigned Hub: {result.AssignedHub}");
        Console.WriteLine($"Device ID: {result.DeviceId}\n");

        if (result.Status != ProvisioningRegistrationStatusType.Assigned)
        {
            throw new Exception($"Provisioning failed: {result.Status}");
        }

        // Create device client for IoT Hub
        Console.WriteLine("=== Connecting to IoT Hub ===");
        var auth = new DeviceAuthenticationWithX509Certificate(
            result.DeviceId,
            certificate);

        var deviceClient = DeviceClient.Create(
            result.AssignedHub,
            auth,
            TransportType.Mqtt);

        await deviceClient.OpenAsync();
        Console.WriteLine("Connected to IoT Hub!\n");

        return deviceClient;
    }

    static async Task SendTelemetry(DeviceClient deviceClient)
    {
        Console.WriteLine("=== Sending Telemetry ===");

        for (int i = 1; i <= 10; i++)
        {
            var telemetry = new
            {
                temperature = 20 + Random.Shared.NextDouble() * 10,
                humidity = 60 + Random.Shared.NextDouble() * 20,
                timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(telemetry);
            var message = new Message(Encoding.UTF8.GetBytes(json))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            };

            await deviceClient.SendEventAsync(message);
            Console.WriteLine($"[{i}] Sent: Temp={telemetry.temperature:F1}°C, " +
                            $"Humidity={telemetry.humidity:F1}%");

            await Task.Delay(5000); // Wait 5 seconds
        }

        Console.WriteLine("\nTelemetry complete!");
    }
}

// Configuration classes
class Config
{
    public DpsConfig DPS { get; set; } = new();
}

class DpsConfig
{
    public string ProvisioningHost { get; set; } = "";
    public string IdScope { get; set; } = "";
    public string RegistrationId { get; set; } = "";
    public string CertificatePath { get; set; } = "";
    public string CertificateKeyPath { get; set; } = "";
}
```

### Run the Device

Finally we run the device simulator to see the complete provisioning flow in action.

```bash
dotnet run
```

**Expected output:**
```
=== IoT Device Simulator with X.509 ===

Certificate loaded: CN=my-device-001
Valid: 1/21/2026 10:00:00 AM to 1/21/2027 10:00:00 AM

=== Starting DPS Provisioning ===
Host: global.azure-devices-provisioning.net
ID Scope: 0ne00123ABC
Registration ID: my-device-001

Registering with DPS...
Status: Assigned
Assigned Hub: my-iot-hub-x509.azure-devices.net
Device ID: my-device-001

=== Connecting to IoT Hub ===
Connected to IoT Hub!

=== Sending Telemetry ===
[1] Sent: Temp=24.3°C, Humidity=67.2%
[2] Sent: Temp=26.7°C, Humidity=71.5%
...
```

## What's Happening Behind the Scenes

### 1. Certificate Loading
```
Device loads:
- device.pem (public certificate)
- device.key (private key)
→ Creates X509Certificate2 object
```

### 2. DPS Connection
```
Device connects to: global.azure-devices-provisioning.net:8883
Protocol: MQTT with TLS
Authentication: Client certificate (mutual TLS)
```

### 3. DPS Validation
```
DPS checks:
✓ Certificate signature is valid
✓ Certificate chains to verified CA
✓ Certificate not expired
✓ Enrollment group exists and enabled
→ Device is authenticated
```

### 4. Hub Assignment
```
DPS determines target IoT Hub:
- Uses allocation policy (hashed/geolatency/etc)
- Creates device identity in IoT Hub
- Returns assignment to device
```

### 5. IoT Hub Connection
```
Device connects to: {assigned-hub}.azure-devices.net:8883
Protocol: MQTT with TLS
Authentication: Same X.509 certificate
→ Ready to send telemetry
```

## Verify Device Registration

### In Azure Portal

1. Navigate to your **IoT Hub**
2. Click **Devices** in the left menu
3. You should see your device: `my-device-001`
4. Click on the device

**Device details:**
- Authentication type: **X.509 CA Signed**
- Primary Thumbprint: (empty - using CA)
- Status: **Enabled**
- Connection state: **Connected**

### Via Azure CLI

```powershell
# List devices in IoT Hub
az iot hub device-identity list `
  --hub-name "my-iot-hub-x509" `
  --query "[].{DeviceID:deviceId, Auth:authentication.type, Status:status}" `
  -o table
```

**Output:**
```
DeviceID        Auth              Status
--------------  ----------------  --------
my-device-001   certificateAuthority  enabled
```

### View DPS Registration Record

```powershell
az iot dps enrollment-group registration list `
  --dps-name "my-dps-x509" `
  --resource-group "iot-x509-demo-rg" `
  --enrollment-id "my-device-group" `
  --query "[].{DeviceID:deviceId, Hub:assignedHub, Created:createdDateTimeUtc}" `
  -o table
```

## Monitor Telemetry

### Using Azure CLI

```powershell
# Monitor telemetry in real-time
az iot hub monitor-events `
  --hub-name "my-iot-hub-x509" `
  --device-id "my-device-001"
```

**Output:**
```json
{
  "event": {
    "origin": "my-device-001",
    "payload": {
      "temperature": 24.3,
      "humidity": 67.2,
      "timestamp": "2026-01-21T10:30:00Z"
    }
  }
}
```

### Using Azure Portal

1. Navigate to your **IoT Hub**
2. Click on your device (`my-device-001`)
3. Click **Device Twin** to see reported properties
4. Use **Azure IoT Explorer** for advanced monitoring

## Troubleshooting

### Error: "Certificate verification failed"

**Symptom:** Device can't connect to DPS

**Solutions:**
- Verify CA certificates are verified in DPS
- Check certificate hasn't expired: `openssl x509 -in certs/device/device.pem -noout -dates`
- Verify chain is correct: `openssl verify -CAfile certs/root/root.pem -untrusted certs/intermediate/intermediate.pem certs/device/device.pem`

### Error: "Registration ID mismatch"

**Symptom:** `RegistrationId in request does not match certificate CN`

**Solution:** Ensure `RegistrationId` in config matches certificate CN:
```powershell
openssl x509 -in certs/device/device.pem -noout -subject
# Subject: CN = my-device-001
```

### Error: "Device not found in enrollment group"

**Symptom:** Device rejected during provisioning

**Solutions:**
- Verify enrollment group is enabled
- Check device cert is signed by enrolled CA
- Confirm CA name in enrollment matches DPS certificate name

### Error: "Connection timeout"

**Symptom:** Can't reach DPS or IoT Hub

**Solutions:**
- Check network connectivity
- Verify firewall allows MQTT port 8883
- Confirm DPS endpoint: `global.azure-devices-provisioning.net`

## Best Practices

### Certificate Management
- ✅ Store private keys securely (not in code)
- ✅ Use environment variables for sensitive paths
- ✅ Implement certificate renewal before expiration
- ✅ Log certificate details on startup (CN, expiry)

### Error Handling
- ✅ Implement retry logic for transient failures
- ✅ Log provisioning results for debugging
- ✅ Handle certificate expiration gracefully
- ✅ Monitor connection state

### Production Considerations
- ✅ Use hardware security modules (HSM) for keys
- ✅ Implement certificate rotation
- ✅ Monitor certificate expiration dates
- ✅ Set up alerts for provisioning failures

## Next Steps

See the Python implementation for comparison, or proceed to the complete provisioning flow walkthrough.

---

[← Previous: Creating Enrollment Groups](06-creating-enrollment-groups.md) | [Next: Setting up a Simulated Device (Python) →](07b-setting-up-python-device.md)
