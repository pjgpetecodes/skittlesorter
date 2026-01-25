# A Primer on DPS

[← Previous: Introduction](00-introduction.md) | [Next: Creating DPS and IoT Hub →](02-creating-azure-resources.md)

---

> **Note:** This guide uses **PowerShell** for all commands and scripts. While Azure CLI commands work identically in bash/zsh, PowerShell is recommended for Windows environments and provides consistent cross-platform support. If you're using Linux/macOS, you can install [PowerShell Core](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) or adapt the commands to bash syntax.

---

## What is Device Provisioning Service?

Let's start with the basics. Before we write code or create Azure resources, you need to understand what DPS does and why it matters for your IoT deployment.

Azure IoT Hub Device Provisioning Service (DPS) is a helper service for IoT Hub that enables zero-touch, just-in-time provisioning to the right IoT hub without requiring human intervention.

## Why Use DPS?

You might be wondering: "Can't I just register devices manually in IoT Hub?" You can, but let's see why that doesn't scale:

### Traditional Approach (Manual Registration)
- Register each device manually in IoT Hub
- Hardcode connection strings in device firmware
- Difficult to reassign devices to different hubs
- Doesn't scale beyond a few devices
- Manual certificate management and rotation

### DPS Approach (Zero-Touch Provisioning)
- Devices automatically register themselves
- No connection strings hardcoded
- Devices can be reassigned dynamically
- Scales to millions of devices
- Automated certificate issuance and lifecycle management

## Key Benefits

Now that you understand the difference, here are the concrete advantages of using DPS:

### Scale
Provision thousands or millions of devices without manual intervention. Devices can self-register when they first come online.

### Security
No connection strings hardcoded in device firmware. Devices use cryptographic attestation (symmetric keys, TPM, or X.509 certificates) to prove identity.

### Flexibility
Devices can be reassigned to different IoT Hubs based on business logic, geolocation, or load balancing.

### Multi-tenancy
Different devices can automatically be directed to different IoT Hubs based on enrollment configuration.

### 🆕 Automated Certificate Management (2025 Feature)
DPS can now issue X.509 certificates dynamically during provisioning using Certificate Signing Requests (CSR). No need to run your own Certificate Authority!

## What's New: Microsoft Certificate Management

Starting with the `2025-07-01-preview` API, DPS introduces **certificate issuance** capabilities:

### Traditional X.509 Workflow
```
1. Set up your own Certificate Authority (CA)
2. Generate root and intermediate certificates
3. Configure DPS to trust your CA
4. Pre-generate device certificates
5. Install certificates on devices
6. Manually handle certificate rotation
```

### 🆕 New CSR-Based Workflow
```
1. Configure credential policy in Azure Device Registry (ADR)
2. Create DPS enrollment group
3. Device generates private key + CSR during provisioning
4. DPS validates device identity
5. Microsoft signs CSR and returns certificate
6. Device uses certificate for IoT Hub connection
7. Automatic certificate rotation via ADR policies
```

**Key advantages:**
- No need to manage your own CA infrastructure
- Private keys never leave the device
- Certificates issued just-in-time
- Automated lifecycle management
- Reduced operational complexity

## Azure Device Registry (ADR) Integration

Azure Device Registry is a new service that works alongside DPS and IoT Hub:

### What is ADR?

ADR is a **device identity and management layer** that:
- Stores device metadata, attributes, and tags
- Manages credential policies for certificate issuance
- Decouples device identity from IoT Hub
- Enables multi-hub scenarios
- Provides query and update APIs

### Why ADR Matters

In traditional IoT Hub deployments:
- Device identity is tightly coupled to a specific IoT Hub
- Moving devices between hubs is complex
- Credential management is manual
- Metadata is limited

With ADR:
- ✅ Device identity is hub-independent
- ✅ Credential policies automate certificate management
- ✅ Rich metadata and tagging support
- ✅ Query devices across multiple hubs
- ✅ Update device properties programmatically

### ADR Credential Policies

Credential policies define how certificates are issued:

```json
{
  "name": "cert-policy",
  "type": "x509CA",
  "validity": "P30D",  // 30 days
  "renewalWindow": "P7D",  // Renew 7 days before expiry
  "caReference": "microsoft-managed"
}
```

During provisioning:
1. Device submits CSR to DPS
2. DPS uses credential policy from ADR
3. Certificate is signed by Microsoft-managed CA
4. Device receives certificate chain
5. Certificate auto-renews based on policy

## Attestation Methods

Attestation is how devices prove their identity to DPS. Think of it like showing ID before boarding a flight. DPS supports three different "ID types":

### 1. Symmetric Key
- **Simplest method**
- Uses shared secret (enrollment group key)
- Device derives individual key: `HMACSHA256(groupKey, registrationId)`
- Good for development and testing
- Less secure than certificate-based methods
- 🆕 **Can be combined with CSR** - authenticate with symmetric key, receive X.509 certificate

### 2. TPM (Trusted Platform Module)
- Uses hardware security module
- Very secure
- Requires TPM chip on device
- Unique per device
- Not covered in this series

### 3. X.509 Certificate
- **Industry standard for security**
- Uses public key infrastructure (PKI)
- Supports certificate rotation
- Scalable with enrollment groups
- **Two approaches:**
  - **Traditional:** Pre-provisioned certificates
  - **🆕 CSR-based:** Certificates issued during provisioning
- **This is what we'll focus on**

## High-Level Provisioning Flow with ADR

Here's how the complete flow works with the new features:

```
┌──────────┐
│  Device  │  1. Generate CSR + private key
└────┬─────┘
     │
     │ 2. Connect to DPS with attestation
     │    (symmetric key OR X.509 bootstrap cert)
     │
     ▼
┌─────────────────┐
│       DPS       │  3. Validate attestation
│                 │  4. Check enrollment group
└────┬────────────┘  5. Query ADR for credential policy
     │
     │ 6. Submit CSR to ADR for signing
     │
     ▼
┌─────────────────┐
│       ADR       │  7. Sign CSR using credential policy
│                 │  8. Return certificate chain
└────┬────────────┘
     │
     │ 9. Return to DPS
     │
     ▼
┌─────────────────┐
│       DPS       │  10. Determine target IoT Hub
│                 │  11. Return assignment + certificate
└────┬────────────┘
     │
     │ 12. Return to device
     │
     ▼
┌──────────┐
│  Device  │  13. Install certificate
└────┬─────┘  14. Connect to assigned IoT Hub with X.509
     │
     ▼
┌─────────────┐
│  IoT Hub    │  15. Device registered and authenticated
└─────────────┘  16. Device can send telemetry
```

## Dual Attestation: Symmetric Key + X.509

One powerful pattern enabled by the new API:

1. **Phase 1 - Provisioning:** Device authenticates to DPS using **symmetric key** (simple, pre-shared)
2. **Phase 2 - Operation:** Device connects to IoT Hub using **X.509 certificate** (secure, Microsoft-issued)

**Benefits:**
- Easy initial provisioning (no pre-generated certificates)
- Secure operational communication (X.509)
- No need to distribute device certificates ahead of time
- Best of both worlds

## When to Use DPS

✅ **Use DPS when:**
- Deploying many devices (10+)
- Devices need to be reassigned between hubs
- You want zero-touch provisioning
- Security is important (no hardcoded secrets)
- You need multi-tenancy support
- You want automated certificate management
- 🆕 You want Microsoft-managed certificate issuance

❌ **Skip DPS when:**
- Only a few devices (< 10)
- Devices never move between hubs
- Prototyping with connection strings is acceptable
- You don't need automated provisioning
- You prefer managing your own CA infrastructure

## Preview API Status

The features covered in this series use the **`2025-07-01-preview` API**:

- ✅ CSR-based certificate issuance
- ✅ ADR credential policies
- ✅ DPS MQTT protocol support
- ✅ Microsoft-managed CA

As of January 2026, these features are in **preview**. The official Microsoft SDKs don't yet support them, which is why this project includes a **custom DPS framework** that implements the MQTT protocol directly.

> **Note:** Always check [Microsoft's documentation](https://learn.microsoft.com/azure/iot-dps/) for the latest API status and general availability dates.

## Next Steps

In the following sections, we'll:
1. Create DPS, IoT Hub, and ADR instances
2. Configure credential policies for certificate issuance
3. Set up enrollment groups (symmetric key and X.509)
4. Build a .NET device application with custom DPS framework
5. Implement CSR generation and certificate management
6. Integrate with ADR for device management

Let's get started!

---

[Next: Creating DPS and IoT Hub →](02-creating-azure-resources.md)
