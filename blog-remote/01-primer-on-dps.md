## **A DPS Primer**

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

Welcome back to this series of posts exploring the latest features of Azure IoT Hub, Device Provisioning Service (DPS), and Azure Device Registration (ADR).

The previous post introduced the new features and gave an overview of what we can expect in this post series.

In this post, we'll dive deeper in the Azure Device Provisioning Service and how we can use the new features.

---

> **Note:** This guide uses **PowerShell** for all commands and scripts. While Azure CLI commands work identically in bash/zsh, PowerShell is recommended for Windows environments and provides consistent cross-platform support. If you're using Linux/macOS, you can install [PowerShell Core](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) or adapt the commands to bash syntax.

---

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
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2" target="_blank" rel="noreferrer noopener">A DPS Primer</a> (This Post)</li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3" target="_blank" rel="noreferrer noopener">Creating DPS, IoT Hub and ADR Instances</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4" target="_blank" rel="noreferrer noopener">Understanding X.509 and CSR Workflows</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5" target="_blank" rel="noreferrer noopener">The Device Application</a></li>
<!-- /wp:list-item --></ol>
<!-- /wp:list -->
```

### What is Device Provisioning Service?

Let's start with the basics. Before we write code or create Azure resources, you need to understand what DPS does and why it matters for your IoT deployment.

Azure IoT Hub Device Provisioning Service (DPS) is a helper service for IoT Hub that enables zero-touch, just-in-time provisioning to the right IoT hub without requiring human intervention.

### Why Use DPS?

You might be wondering: "Can't I just register devices manually in IoT Hub?" You can, but let's see why that doesn't scale:

#### Traditional Approach (Manual Registration)

-   Register each device manually in IoT Hub
-   Hardcode connection strings in device firmware
-   Difficult to reassign devices to different hubs
-   Doesn't scale beyond a few devices
-   Manual certificate management and rotation

#### DPS Approach (Zero-Touch Provisioning)

-   Devices automatically register themselves
-   No connection strings hardcoded
-   Devices can be reassigned dynamically
-   Scales to millions of devices
-   Automated certificate issuance and lifecycle management

### Key Benefits

Now that you understand the difference, here are the concrete advantages of using DPS:

-   **Scale:** Provision thousands or millions of devices without manual intervention. Devices can self-register when they first come online.
-   **Security:** No connection strings hardcoded in device firmware. Devices use cryptographic attestation (symmetric keys, TPM, or X.509 certificates) to prove identity.
-   **Flexibility:** Devices can be reassigned to different IoT Hubs based on business logic, geolocation, or load balancing.
-   **Multi-tenancy:** Different devices can automatically be directed to different IoT Hubs based on enrollment configuration.
-   **Separation:** Separation between bootstrap and operational credentials for devices.

#### 🆕 Automated Certificate Management (2025 Feature)

DPS can now issue X.509 certificates dynamically during provisioning using Certificate Signing Requests (CSR). No need to run your own Certificate Authority!

### What's New: Microsoft Certificate Management

The custom device code in this repository uses the `2025-07-01-preview` API
for its CSR provisioning example. The current service workflow is that DPS
validates the device's onboarding credential and coordinates issuance of an
**operational certificate** from the ADR namespace's issuing CA:

#### Traditional X.509 Workflow

```plaintext
┌─────────────────────────────┐
│       Your CA (PKI)         │
└──────────────┬──────────────┘
               │ create
               ▼
┌─────────────────────────────┐
│ Root & Intermediate Certs   │
└──────────────┬──────────────┘
               │ trust
               ▼
┌─────────────────────────────┐
│            DPS              │
└──────────────┬──────────────┘
               │ pre-provision
               ▼
┌─────────────────────────────┐
│  Device Certificates (per   │
│  device, generated upfront) │
└──────────────┬──────────────┘
               │ install
               ▼
┌─────────────────────────────┐
│           Devices           │
└──────────────┬──────────────┘
               │ connect using X.509
               ▼
┌─────────────────────────────┐
│           IoT Hub           │
└─────────────────────────────┘

Notes:
- Certs are created ahead of time and installed on devices.
- Rotation is manual: generate, distribute, and update per device.
```

#### 🆕 New CSR-Based Workflow

```plaintext
┌─────────────────────────────┐          ┌─────────────────────────────┐
│     Azure Device Registry   │          │            DPS              │
│ (Credential Policy: x509CA) │◄─────────┤   Enrollment Group exists   │
└──────────────┬──────────────┘  query   └──────────────┬──────────────┘
               │                                        ▲ validate
               │                                        │
               │                    CSR + attestation   │
┌──────────────▼──────────────┐    (symmetric/X.509)    │
│           Device            │ ────────────────────────┘
│ 1) Generate keypair + CSR   │
└──────────────┬──────────────┘
               │ submit CSR
               ▼                 use
┌─────────────────────────────┐ policy   ┌─────────────────────────────┐
│            ADR              │─────────►│  Microsoft-managed CA       │
│ 2) Sign CSR per policy      │          │  3) Issue certificate chain │
└──────────────┬──────────────┘          └──────────────┬──────────────┘
               │ return cert chain                      │
               └────────────────────────────────────────┘
               │            to DPS → to Device
               │
┌──────────────▼──────────────┐
│           Device            │
│ 4) Install cert chain       │
│ 5) Connect to IoT Hub       │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│           IoT Hub           │
└─────────────────────────────┘

Notes:
- Private key never leaves the device.
- The certificate policy controls certificate validity.
- Current IoT Hub workflows support operational-certificate renewal without
  reprovisioning; follow the current service documentation for the device flow.
```

**Key advantages:**

-   No need to manage your own CA infrastructure
-   Private keys never leave the device
-   Certificates issued just-in-time
-   Automated lifecycle management
-   Reduced operational complexity

### Azure Device Registry (ADR) Integration

Azure Device Registry (ADR) is Microsoft's **centralized device registry and
management plane**. It represents IoT Hub devices as Azure Resource Manager
resources, enabling governance through RBAC, tags, resource groups, and
cross-hub queries with Azure Resource Graph.

ADR is generally available for Azure IoT Operations. Its integration with
Azure IoT Hub is a separate **public preview**. Each IoT Hub links to one ADR
namespace, while multiple hubs can share a namespace. An existing IoT Hub
can't be upgraded to add ADR integration; create a new hub for the preview.

#### What is ADR?

ADR is a **device identity and management layer** that:

-   Stores device metadata, attributes, and tags
-   Manages credential policies for certificate issuance
-   Decouples device identity from IoT Hub
-   Enables multi-hub scenarios
-   Provides query and update APIs

### What is an ADR Namespace?

An ADR namespace establishes a management and security boundary for devices represented in ADR.

A namespace can include devices connecting to different IoT Hub instances, and is where you can enable new features like Microsoft-backed X.509 certificate management (preview).

#### Why ADR Matters

In traditional IoT Hub deployments:

-   Device identity is tightly coupled to a specific IoT Hub
-   Moving devices between hubs is complex
-   Credential management is manual
-   Metadata is limited

With ADR:

-   ✅ Devices are represented and governed as Azure resources
-   ✅ Namespaces provide a shared registry and security boundary
-   ✅ Credential policies support certificate issuance
-   ✅ Query and manage devices across hubs sharing a namespace

### Certificate Management: Onboarding and Operational Credentials

Certificate management handles **operational X.509 certificates** used by a
device to authenticate to IoT Hub after provisioning. It does not issue or
manage the device's onboarding credential. A device first authenticates to DPS
with an onboarding method such as a symmetric key, TPM, or an existing X.509
certificate, then submits a CSR as part of the provisioning transaction.
DPS validates the onboarding credential and selects the policy associated
with the enrollment; the namespace's ADR issuing CA signs the operational
certificate. The device private key remains on the device.

Certificate management is optional, but **DPS is required for all IoT Hub
preview scenarios**, even when certificate management isn't enabled.
Operational certificates can be renewed and revoked, and ADR manages CA
rotation. IoT Hub integration remains in public preview and isn't recommended
for production workloads.

### Attestation Methods

Attestation is how devices prove their identity to DPS. Think of it like showing ID before boarding a flight. DPS supports three different "ID types":

#### 1\. Symmetric Key

-   **Simplest method**
-   Uses shared secret (enrollment group key)
-   Device derives individual key: `HMACSHA256(groupKey, registrationId)`
-   Good for development and testing
-   Less secure than certificate-based methods
-   🆕 **Can be combined with CSR** - authenticate with symmetric key, receive X.509 certificate

#### 2\. TPM (Trusted Platform Module)

-   Uses hardware security module
-   Very secure
-   Requires TPM chip on device
-   Unique per device
-   Not covered in this series

#### 3\. X.509 Certificate

-   **Industry standard for security**
-   Uses public key infrastructure (PKI)
-   Supports certificate rotation
-   Scalable with enrollment groups
-   **Two approaches:**
    -   **Traditional:** Pre-provisioned certificates
    -   **🆕 CSR-based:** Certificates issued during provisioning
-   **This is what we'll focus on**

### High-Level Provisioning Flow with ADR

Here's how the complete flow works with the new features:

```plaintext
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

### Dual Attestation: Symmetric Key + X.509

One powerful pattern enabled by the new API:

1.  **Phase 1 - Provisioning:** Device authenticates to DPS using **symmetric key** (simple, pre-shared)
2.  **Phase 2 - Operation:** Device connects to IoT Hub using **X.509 certificate** (secure, Microsoft-issued)

**Benefits:**

-   Easy initial provisioning (no pre-generated certificates)
-   Secure operational communication (X.509)
-   No need to distribute device certificates ahead of time
-   Best of both worlds

### When to Use DPS

✅ **Use DPS when:**

-   Deploying many devices (10+)
-   Devices need to be reassigned between hubs
-   You want zero-touch provisioning
-   Security is important (no hardcoded secrets)
-   You need multi-tenancy support
-   You want automated certificate management
-   🆕 You want Microsoft-managed certificate issuance

🤔 **Consider if DPS is right for you if:**

-   Only a few devices (< 10)
-   Devices never move between hubs
-   Prototyping with connection strings is acceptable

### Preview API Status

The sample application in this series uses the **`2025-07-01-preview` API**:

-   ✅ CSR-based certificate issuance
-   ✅ ADR credential policies
-   ✅ DPS MQTT protocol support
-   ✅ Microsoft-managed CA

IoT Hub integration with ADR and certificate management is still in **public
preview** and isn't recommended for production workloads. The custom DPS
framework is this project's implementation choice; don't treat its API version
or client-library assumptions as a general Microsoft SDK support statement.

> **Note:** Check [Microsoft's current IoT Hub preview guidance](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new) before deployment.

### Next Steps

In the following sections, we'll:

1.  Create DPS, IoT Hub, and ADR instances
2.  Configure credential policies for certificate issuance
3.  Set up enrollment groups (symmetric key and X.509)
4.  Build a .NET device application with custom DPS framework
5.  Implement CSR generation and certificate management
6.  Integrate with ADR for device management

[Next: Creating DPS, IoT Hub and ADR Instances >](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/)
