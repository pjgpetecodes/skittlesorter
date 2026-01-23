# Provisioning the Device Through DPS

[← Previous: Setting up a Simulated Device (Python)](07b-setting-up-python-device.md)

---

## Provisioning Flow: From Certificate to Connected Device

This section traces the complete provisioning sequence. We'll examine what happens at each step as the device initializes its certificate, authenticates with DPS, receives its hub assignment, and connects to IoT Hub.

## The Complete Provisioning Flow

Now let's walk through exactly what happens when a device provisions through DPS using X.509 certificates. We'll observe each step from both the device and Azure perspectives.

## Prerequisites

- Device simulator from previous section ready to run
- DPS configured with enrollment group
- Certificates generated and verified
- Network connectivity to Azure

## The Flow: Overview Diagram

```
┌──────────┐
│  Device  │
└────┬─────┘
     │ 1. Load X.509 certificate
     │
     ▼
┌──────────┐
│  Device  │ 2. Connect to DPS (TLS + client cert)
└────┬─────┘    global.azure-devices-provisioning.net:8883
     │
     ▼
┌─────────────────┐
│       DPS       │ 3. Validate certificate chain
│                 │    - Check signature
│                 │    - Verify CA is trusted
│                 │    - Check expiration
└────┬────────────┘
     │ 4. Certificate valid ✓
     │
     ▼
┌─────────────────┐
│       DPS       │ 5. Find matching enrollment group
│                 │    - CA matches intermediate CA
│                 │    - Enrollment enabled
└────┬────────────┘
     │ 6. Enrollment found ✓
     │
     ▼
┌─────────────────┐
│       DPS       │ 7. Determine target IoT Hub
│                 │    - Apply allocation policy
│                 │    - Select hub
└────┬────────────┘
     │ 8. Hub assigned ✓
     │
     ▼
┌─────────────────┐
│    IoT Hub      │ 9. Create device identity
│                 │    - Device ID = registration ID
│                 │    - Auth type = X.509 CA Signed
└────┬────────────┘
     │ 10. Device created ✓
     │
     ▼
┌─────────────────┐
│       DPS       │ 11. Return assignment to device
│                 │     assignedHub: my-hub.azure-devices.net
│                 │     deviceId: my-device-001
└────┬────────────┘
     │
     ▼
┌──────────┐
│  Device  │ 12. Connect to assigned IoT Hub
└────┬─────┘     - Same X.509 certificate
     │            - MQTT over TLS
     ▼
┌─────────────────┐
│    IoT Hub      │ 13. Authenticate device
│                 │     - Validate certificate
│                 │     - Device ID matches
└────┬────────────┘
     │ 14. Connection established ✓
     │
     ▼
┌──────────┐
│  Device  │ 15. Device is ready!
└──────────┘     - Send telemetry
                 - Receive commands
                 - Update twin
```

## Step-by-Step Walkthrough

### Step 1-2: Device Initiates Connection

**Device action:**
```csharp
// Load certificate
var cert = X509Certificate2.CreateFromPem(certPem, keyPem);

// Create DPS client
var security = new SecurityProviderX509Certificate(cert);
var transport = new ProvisioningTransportHandlerMqtt();
var client = ProvisioningDeviceClient.Create(
    "global.azure-devices-provisioning.net",
    idScope,
    security,
    transport
);
```

**What's happening:**
- Device reads certificate and private key from disk
- Creates TLS client with client certificate
- Connects to DPS endpoint on port 8883 (MQTT over TLS)

**Network traffic:**
```
Client → DPS: TLS ClientHello
DPS → Client: TLS ServerHello, Certificate Request
Client → DPS: TLS Certificate (device cert + chain)
Client → DPS: TLS Finished (signed with private key)
DPS validates signature → TLS established ✓
```

### Step 3-4: DPS Validates Certificate

**DPS performs these checks:**

1. **Certificate parsing**
   ```
   Subject: CN=my-device-001
   Issuer: CN=My-Intermediate-CA
   Validity: Jan 21 2026 - Jan 21 2027
   Signature Algorithm: sha256WithRSAEncryption
   ```

2. **Chain validation**
   ```
   Device cert → Intermediate CA → Root CA
   - Device cert signed by intermediate? ✓
   - Intermediate signed by root? ✓
   - Root is trusted in DPS? ✓
   ```

3. **Expiration check**
   ```
   Current time: Jan 21 2026 10:30:00
   Not Before: Jan 21 2026 10:00:00 ✓
   Not After: Jan 21 2027 10:00:00 ✓
   ```

4. **Revocation check**
   ```
   Certificate not in revocation list? ✓
   ```

**If any check fails, connection is rejected immediately.**

### Step 5-6: Find Enrollment Group

**DPS logic:**

```sql
-- Pseudo-query
SELECT * FROM EnrollmentGroups
WHERE CAReference = (Issuer of device cert)
  AND Status = 'Enabled'
  AND (DeviceId matches enrollment pattern OR enrollment is group)
```

**Match found:**
```json
{
  "enrollmentGroupId": "my-device-group",
  "attestation": {
    "x509": {
      "caReferences": {
        "primary": "my-intermediate-ca"  // ← Matches device cert issuer
      }
    }
  },
  "provisioningStatus": "enabled"  // ← Enrollment active
}
```

**DPS confirms:**
- ✅ Device certificate signed by enrolled CA
- ✅ Enrollment group is enabled
- ✅ Device allowed to provision

### Step 7-8: Determine Target Hub

**DPS applies allocation policy:**

**Hashed (default):**
```csharp
int hash = ComputeHash(registrationId);
int hubIndex = hash % numberOfLinkedHubs;
assignedHub = linkedHubs[hubIndex];
```

**Result:**
```
Registration ID: my-device-001
Hash: 0x3A4B5C6D
Linked Hubs: [hub1, hub2, hub3]
Index: 0x3A4B5C6D % 3 = 1
Assigned Hub: hub2 (my-iot-hub-x509.azure-devices.net)
```

**Other policies:**
- **GeoLatency**: Chooses closest hub based on device IP
- **Static**: Always same hub
- **Custom**: Calls Azure Function for decision

### Step 9-10: Create Device in IoT Hub

**DPS makes REST call to IoT Hub:**

```http
PUT https://my-iot-hub-x509.azure-devices.net/devices/my-device-001?api-version=2021-04-12
Authorization: SharedAccessSignature (DPS token)
Content-Type: application/json

{
  "deviceId": "my-device-001",
  "authentication": {
    "type": "certificateAuthority",
    "x509Thumbprint": {
      "primaryThumbprint": null,
      "secondaryThumbprint": null
    }
  },
  "status": "enabled"
}
```

**Important:** Auth type is `certificateAuthority`, not `selfSigned`. This means the device uses CA-signed cert, not a specific thumbprint.

**IoT Hub response:**
```json
{
  "deviceId": "my-device-001",
  "status": "enabled",
  "authentication": {
    "type": "certificateAuthority"
  }
}
```

### Step 11: DPS Returns Assignment

**Device receives:**

```csharp
var result = await provisioningClient.RegisterAsync();

// result contents:
{
  "status": "Assigned",
  "assignedHub": "my-iot-hub-x509.azure-devices.net",
  "deviceId": "my-device-001",
  "registrationState": {
    "registrationId": "my-device-001",
    "createdDateTimeUtc": "2026-01-21T10:30:00Z",
    "assignedHub": "my-iot-hub-x509.azure-devices.net",
    "deviceId": "my-device-001",
    "status": "assigned",
    "etag": "AAAAAAFPTRt="
  }
}
```

**Device logs:**
```
[2026-01-21 10:30:15] DPS registration initiated
[2026-01-21 10:30:16] Certificate validation: OK
[2026-01-21 10:30:16] Enrollment group matched: my-device-group
[2026-01-21 10:30:17] Device assigned to hub: my-iot-hub-x509.azure-devices.net
[2026-01-21 10:30:17] Device ID: my-device-001
```

### Step 12-14: Connect to IoT Hub

**Device connects to assigned hub:**

```csharp
var auth = new DeviceAuthenticationWithX509Certificate(
    result.DeviceId,
    certificate
);

var deviceClient = DeviceClient.Create(
    result.AssignedHub,  // my-iot-hub-x509.azure-devices.net
    auth,
    TransportType.Mqtt
);

await deviceClient.OpenAsync();
```

**IoT Hub validation:**
1. Accepts TLS connection with client certificate
2. Validates certificate chain (same as DPS)
3. Checks device exists in registry
4. Confirms auth type is `certificateAuthority`
5. Verifies CN matches device ID
6. Connection established ✓

**MQTT connection:**
```
Client → Hub: CONNECT (client cert in TLS)
Hub → Client: CONNACK (connection accepted)
Client → Hub: SUBSCRIBE $iothub/twin/... (twin updates)
Client → Hub: SUBSCRIBE $iothub/methods/... (direct methods)
Hub → Client: SUBACK (subscriptions confirmed)
```

### Step 15: Device is Ready

**Device can now:**
- ✅ Send telemetry messages
- ✅ Receive cloud-to-device messages
- ✅ Respond to direct methods
- ✅ Update device twin properties
- ✅ Receive desired property updates

## Observing the Flow

### Enable DPS Diagnostics

First we enable diagnostic logging in DPS to observe provisioning events in detail.

```powershell
# Create Log Analytics workspace (if you don't have one)
$workspaceId = az monitor log-analytics workspace create `
  --resource-group "iot-x509-demo-rg" `
  --workspace-name "iot-logs" `
  --query id -o tsv

# Enable DPS diagnostics
az monitor diagnostic-settings create `
  --resource $(az iot dps show --name "my-dps-x509" --resource-group "iot-x509-demo-rg" --query id -o tsv) `
  --name "dps-diagnostics" `
  --workspace $workspaceId `
  --logs '[{"category":"DeviceOperations","enabled":true}]'
```

### Query Provisioning Logs

Next we query the diagnostic logs to see detailed provisioning events and results.

```kql
AzureDiagnostics
| where ResourceType == "IOTHUBPROVISIONINGSERVICES"
| where Category == "DeviceOperations"
| where operationName_s == "Register"
| project TimeGenerated, registrationId_s, resultType, resultDescription, deviceId_s, assignedHub_s
| order by TimeGenerated desc
```

**Example output:**
| TimeGenerated | registrationId_s | resultType | deviceId_s | assignedHub_s |
|---------------|------------------|------------|------------|---------------|
| 2026-01-21 10:30:17 | my-device-001 | Success | my-device-001 | my-iot-hub-x509.azure-devices.net |

### Monitor IoT Hub Connections

Now we monitor real-time events from the device to confirm it's connected and sending telemetry.

```powershell
# Real-time connection monitoring
az iot hub monitor-events `
  --hub-name "my-iot-hub-x509" `
  --device-id "my-device-001" `
  --properties sys anno app
```

**Output:**
```json
{
  "event": {
    "origin": "my-device-001",
    "module": "",
    "interface": "",
    "component": "",
    "payload": {"temperature": 24.3, "humidity": 67.2},
    "annotations": {
      "iothub-connection-device-id": "my-device-001",
      "iothub-connection-auth-method": "X509CA",
      "iothub-connection-auth-generation-id": "638412345678901234"
    }
  }
}
```

Note: `"iothub-connection-auth-method": "X509CA"` confirms certificate authentication.

## Timing and Performance

**Typical provisioning timeline:**

| Step | Duration | Notes |
|------|----------|-------|
| Load certificate | < 10ms | Reading from disk |
| TLS handshake with DPS | 200-500ms | Network latency dependent |
| Certificate validation | 50-100ms | Chain verification |
| Enrollment lookup | 10-50ms | Database query |
| Hub assignment | 10-30ms | Allocation policy execution |
| Device creation in hub | 100-200ms | REST API call |
| Return assignment | 50-100ms | Network response |
| **Total DPS provisioning** | **420-980ms** | **~ 0.5-1 second** |
| Connect to IoT Hub | 200-400ms | TLS + MQTT handshake |
| **Total time to ready** | **620-1380ms** | **~ 0.6-1.4 seconds** |

**After first provisioning:**
- Device can cache assigned hub
- Subsequent connections skip DPS (direct to hub)
- Connection time: ~200-400ms

## Reprovisioning

Devices can reprovision to update their hub assignment.

**Trigger reprovisioning:**
```csharp
// Set reprovisioning policy in enrollment group
az iot dps enrollment-group update `
  --enrollment-id "my-device-group" `
  --reprovisioning-policy "reprovisionandmigratedata"

// Device reprovisiones on next registration call
var result = await provisioningClient.RegisterAsync();
// May get assigned to different hub
```

**Use cases:**
- Load balancing: Move devices between hubs
- Geo-distribution: Reassign based on location
- Hub migration: Move all devices to new hub
- Disaster recovery: Failover to backup hub

## Security Considerations

### What DPS Validates

✅ Certificate signature chain  
✅ Certificate not expired  
✅ CA is verified in DPS  
✅ Enrollment group enabled  
✅ Device presenting valid certificate  

### What DPS Does NOT Validate

❌ Device location (unless custom allocation)  
❌ Device hardware/firmware version  
❌ Previous connection history  

### Additional Security

**Implement in your solution:**
- Monitor for unusual provisioning patterns
- Alert on first-time devices
- Validate device properties during provisioning
- Use custom allocation policy for business logic
- Implement certificate revocation checking

## Troubleshooting Guide

### Device Logs Show "Certificate Validation Failed"

**Check:**
```bash
# Verify cert chain locally
openssl verify -CAfile certs/root/root.pem \
  -untrusted certs/intermediate/intermediate.pem \
  certs/device/device.pem

# Check expiration
openssl x509 -in certs/device/device.pem -noout -dates
```

**In DPS:**
```powershell
# Confirm CA is verified
az iot dps certificate show --certificate-name "my-intermediate-ca" --query "properties.isVerified"
```

### Device Logs Show "Enrollment Not Found"

**Check:**
```powershell
# List enrollments
az iot dps enrollment-group list --query "[].{Name:enrollmentGroupId, CA:attestation.x509.caReferences.primary}"

# Verify CA name matches
az iot dps certificate list --query "[].{Name:name, Subject:properties.subject}"
```

### Device Assigned but Can't Connect to Hub

**Check:**
```powershell
# Verify device exists in hub
az iot hub device-identity show --device-id "my-device-001"

# Check auth type
az iot hub device-identity show --device-id "my-device-001" --query "authentication.type"
# Should be: "certificateAuthority"
```

## What We've Accomplished

🎉 **Complete end-to-end flow:**

✅ Device loads X.509 certificate  
✅ Connects to DPS with certificate authentication  
✅ DPS validates certificate chain  
✅ DPS finds matching enrollment group  
✅ DPS assigns device to IoT Hub  
✅ Device created in IoT Hub automatically  
✅ Device connects to IoT Hub with same certificate  
✅ Device sends telemetry successfully  

**No connection strings. No manual registration. Fully automated.**

## Production Readiness Checklist

Before deploying to production:

- [ ] Certificate expiration monitoring in place
- [ ] Certificate rotation strategy defined
- [ ] CA private keys stored securely (HSM)
- [ ] DPS diagnostics enabled and monitored
- [ ] Alert rules configured for provisioning failures
- [ ] Reprovisioning policy tested
- [ ] Device reprovi logic implemented
- [ ] Certificate revocation process documented
- [ ] Backup and recovery procedures tested
- [ ] Load testing completed for scale

## Conclusion

X.509 certificate attestation with DPS provides:
- **Zero-touch provisioning**: Devices self-register
- **Strong security**: Cryptographic authentication
- **Scalability**: One enrollment group, unlimited devices
- **Flexibility**: Reprovisioning and hub reassignment
- **No secrets in device**: Only certificates (which can be public)

This approach is production-ready and used by major IoT deployments worldwide.

---

[← Previous: Setting up a Simulated Device (Python)](07b-setting-up-python-device.md) | [Back to Start](01-primer-on-dps.md)
