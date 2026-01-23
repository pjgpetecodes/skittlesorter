# Setting up a Simulated IoT Device (Python)

[← Previous: Setting up a Simulated Device (C#)](07a-setting-up-csharp-device.md) | [Next: Provisioning Flow →](08-provisioning-flow.md)

---

## Device-Side Implementation with Python

Up until now, all configuration has been cloud-side setup. Now we implement the device code that will load its certificate and automatically provision itself through DPS using Python.

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
- Python 3.7+ installed

## Python Implementation

### Install Required Packages

First we install the necessary Azure IoT SDK packages for Python.

```bash
pip install azure-iot-device azure-iot-provisioning-device-client
```

### Configuration File

Next we create a configuration file with our DPS details and certificate paths.

Create `config.json`:

```json
{
  "provisioning_host": "global.azure-devices-provisioning.net",
  "id_scope": "0ne00123ABC",
  "registration_id": "my-device-001",
  "cert_file": "../certs/device/device.pem",
  "key_file": "../certs/device/device.key"
}
```

### Device Code

Now we implement the Python device code that loads certificates, provisions through DPS, and sends telemetry.

Create `device_simulator.py`:

```python
import asyncio
import json
import random
from datetime import datetime
from azure.iot.device.aio import ProvisioningDeviceClient, IoTHubDeviceClient
from azure.iot.device import X509

async def main():
    print("=== IoT Device Simulator with X.509 ===\n")
    
    # Load configuration
    with open("config.json") as f:
        config = json.load(f)
    
    # Create X.509 object
    x509 = X509(
        cert_file=config["cert_file"],
        key_file=config["key_file"]
    )
    
    print(f"Certificate loaded from: {config['cert_file']}")
    print(f"Registration ID: {config['registration_id']}\n")
    
    # Provision through DPS
    device_client = await provision_device(
        config["provisioning_host"],
        config["id_scope"],
        config["registration_id"],
        x509
    )
    
    # Send telemetry
    await send_telemetry(device_client)
    
    # Cleanup
    await device_client.disconnect()

async def provision_device(provisioning_host, id_scope, registration_id, x509):
    print("=== Starting DPS Provisioning ===")
    print(f"Host: {provisioning_host}")
    print(f"ID Scope: {id_scope}")
    print(f"Registration ID: {registration_id}\n")
    
    # Create provisioning client
    provisioning_client = ProvisioningDeviceClient.create_from_x509_certificate(
        provisioning_host=provisioning_host,
        registration_id=registration_id,
        id_scope=id_scope,
        x509=x509
    )
    
    # Register with DPS
    print("Registering with DPS...")
    registration_result = await provisioning_client.register()
    
    print(f"Status: {registration_result.status}")
    print(f"Assigned Hub: {registration_result.registration_state.assigned_hub}")
    print(f"Device ID: {registration_result.registration_state.device_id}\n")
    
    if registration_result.status != "assigned":
        raise Exception(f"Provisioning failed: {registration_result.status}")
    
    # Create device client for IoT Hub
    print("=== Connecting to IoT Hub ===")
    device_client = IoTHubDeviceClient.create_from_x509_certificate(
        hostname=registration_result.registration_state.assigned_hub,
        device_id=registration_result.registration_state.device_id,
        x509=x509
    )
    
    await device_client.connect()
    print("Connected to IoT Hub!\n")
    
    return device_client

async def send_telemetry(device_client):
    print("=== Sending Telemetry ===")
    
    for i in range(1, 11):
        telemetry = {
            "temperature": 20 + random.random() * 10,
            "humidity": 60 + random.random() * 20,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        message_json = json.dumps(telemetry)
        await device_client.send_message(message_json)
        
        print(f"[{i}] Sent: Temp={telemetry['temperature']:.1f}°C, "
              f"Humidity={telemetry['humidity']:.1f}%")
        
        await asyncio.sleep(5)
    
    print("\nTelemetry complete!")

if __name__ == "__main__":
    asyncio.run(main())
```

### Run the Device

Finally we run the Python device simulator to see the complete provisioning flow in action.

```bash
python device_simulator.py
```

**Expected output:**
```
=== IoT Device Simulator with X.509 ===

Certificate loaded from: ../certs/device/device.pem
Registration ID: my-device-001

=== Starting DPS Provisioning ===
Host: global.azure-devices-provisioning.net
ID Scope: 0ne00123ABC
Registration ID: my-device-001

Registering with DPS...
Status: assigned
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
→ Creates X509 credential object
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

**Symptom:** `Registration ID in request does not match certificate CN`

**Solution:** Ensure `registration_id` in config matches certificate CN:
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

In the final section, we'll walk through the complete provisioning flow step-by-step and show what's happening at each stage.

---

[← Previous: Setting up a Simulated Device (C#)](07a-setting-up-csharp-device.md) | [Next: Provisioning Flow →](08-provisioning-flow.md)
