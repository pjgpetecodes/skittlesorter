# A Primer on DPS

[← Previous: Introduction](00-introduction.md) | [Next: Creating DPS and IoT Hub →](02-creating-dps-and-iot-hub.md)

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

### DPS Approach (Zero-Touch Provisioning)
- Devices automatically register themselves
- No connection strings hardcoded
- Devices can be reassigned dynamically
- Scales to millions of devices

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

## Attestation Methods

Attestion is how devices prove their identity to DPS. Think of it like showing ID before boarding a flight. DPS supports three different "ID types":

DPS supports three attestation mechanisms:

### 1. Symmetric Key
- Simplest method
- Uses shared secret (enrollment group key)
- Good for development and testing
- Less secure than certificate-based methods

### 2. TPM (Trusted Platform Module)
- Uses hardware security module
- Very secure
- Requires TPM chip on device
- Unique per device

### 3. X.509 Certificate
- Uses public key infrastructure (PKI)
- Industry standard for security
- Supports certificate rotation
- Scalable with enrollment groups
- **This is what we'll focus on**

## High-Level Provisioning Flow

```
┌──────────┐
│  Device  │
└────┬─────┘
     │
     │ 1. Initial connection with attestation
     │
     ▼
┌─────────────────┐
│       DPS       │  2. Validates attestation
│                 │  3. Determines target IoT Hub
└────┬────────────┘
     │
     │ 4. Returns assignment
     │
     ▼
┌──────────┐
│  Device  │  5. Connects to assigned IoT Hub
└────┬─────┘
     │
     ▼
┌─────────────┐
│  IoT Hub    │  6. Device is registered and ready
└─────────────┘
```

## When to Use DPS

✅ **Use DPS when:**
- Deploying many devices
- Devices need to be reassigned between hubs
- You want zero-touch provisioning
- Security is important (no hardcoded secrets)
- You need multi-tenancy support

❌ **Skip DPS when:**
- Only a few devices (< 10)
- Devices never move between hubs
- Prototyping with connection strings is acceptable
- You don't need automated provisioning

## Next Steps

In the following sections, we'll:
1. Create DPS and IoT Hub instances
2. Set up a complete X.509 certificate hierarchy
3. Configure DPS to trust our certificates
4. Create an enrollment group
5. Provision a device using X.509 attestation

Let's get started!

---

[Next: Creating DPS and IoT Hub →](02-creating-dps-and-iot-hub.md)
