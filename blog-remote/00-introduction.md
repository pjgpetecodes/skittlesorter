## **Introduction**

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

Welcome to this comprehensive blog series on building a real-world IoT device using Azure's latest IoT Hub, Azure Device Provisioning Service (DPS) features, along with Microsoft-managed certificate infrastructure.

```wordpress-block
<!-- wp:heading {"textColor":"#f6f6f4"} -->
<h2 class="wp-block-heading has-f-6-f-6-f-4-color has-text-color">Contents:</h2>
<!-- /wp:heading -->
```

```wordpress-block
<!-- wp:list {"ordered":true,"textColor":"#f6f6f4"} -->
<ol class="wp-block-list has-f-6-f-6-f-4-color has-text-color"><!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1" target="_blank" rel="noreferrer noopener">Introduction (This post!)</a></li>
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
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5" target="_blank" rel="noreferrer noopener">The Device Application</a></li>
<!-- /wp:list-item --></ol>
<!-- /wp:list -->
```

### What You'll Build

By the end of this series, you'll have:

-   ✅ A complete IoT device application (Skittle Sorter)
-   ✅ Automated device provisioning with DPS
-   ✅ Microsoft-managed X.509 certificate issuance
-   ✅ Azure Device Registry integration
-   ✅ Practical security patterns and considerations
-   ✅ Full automation scripts

The series uses a **3D-printed Skittle sorter** for the physical build, but it’s **optional** — you can run everything with mocked hardware. Still, it’s a fun build if you want the full experience.

```wordpress-block
<!-- wp:image {"id":4968,"sizeSlug":"full","linkDestination":"media","align":"center"} -->
<figure class="wp-block-image aligncenter size-full"><a href="https://www.petecodes.co.uk/wp-content/uploads/2026/01/GIF_20260112_230237_613.gif"><img src="https://www.petecodes.co.uk/wp-content/uploads/2026/01/GIF_20260112_230237_613.gif" alt="" class="wp-image-4968"/></a></figure>
<!-- /wp:image -->
```

> ⚠️ **Warning:** This guide is for **testing and demo purposes only**. **Do not use self-signed X.509 certificates in production.**

This series focuses on **patterns and principles**. We’ll use self-signed certs to make the learning path simple and reproducible, but the concepts are foundational and transfer directly to production setups. In practice, you’ll replace self-signed certs with **production-grade certificates** issued by a trusted CA (or a managed service).

### Preview Status and Current Guidance

**Current preview status:** Azure IoT Hub integration with Azure Device Registry
(ADR) and Microsoft-backed X.509 certificate management remain in **public
preview** and aren't recommended for production workloads. ADR is generally
available for Azure IoT Operations; its integration with IoT Hub is still in
preview.

ADR represents IoT Hub devices as Azure Resource Manager resources. Each IoT
Hub links to one ADR namespace, and multiple hubs can share a namespace. To try
the IoT Hub preview, create a **new IoT Hub** with ADR integration; there is no
upgrade path for an existing hub. Existing hubs continue to work without
interruption. **DPS is required for every IoT Hub preview scenario**, whether
or not you enable certificate management.

Certificate management is optional. It manages operational X.509 certificates
used to authenticate to IoT Hub after onboarding with a separate credential.
The device submits a CSR during provisioning; DPS validates the onboarding
credential and coordinates issuance from the namespace's ADR certificate
authority. The device's private key remains on the device.

#### Supported Regions

The current supported regions for IoT Hub, ADR, and DPS in this preview are:

- Australia East
- Japan East
- Central US
- UK South

#### Preview Pricing

Starting in October 2026, ADR integration and certificate management are
available at no cost during preview. IoT Hub and DPS are billed at their
standard rates.

### Official Docs & Announcements

-   Azure IoT Hub + ADR preview announcement: [https://techcommunity.microsoft.com/blog/iotblog/azure-iot-hub-with-adr-preview-extending-azure-capabilities-and-certificate-mana/4465265](https://techcommunity.microsoft.com/blog/iotblog/azure-iot-hub-with-adr-preview-extending-azure-capabilities-and-certificate-mana/4465265)
-   Azure IoT Hub “What’s New”: [https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new)
-   Azure Device Registry integration: [https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-overview](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-overview)
-   Certificate management: [https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview)
-   Current deployment steps: [https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)

The sample application later in this series uses a custom DPS framework over
MQTT. That is an implementation detail of this project, not a requirement for
every IoT Hub or ADR deployment. Check the current service documentation before
relying on sample API-version or client-library details.

### Learning the Traditional DPS Approach

If you're interested in learning the traditional DPS workflow with self-signed X.509 certificates (without the new ADR and certificate management features), I've written a series of blog posts covering that approach:

[Using Azure Device Provisioning Service with Self-Signed X.509 Certificates](https://www.petecodes.co.uk/using-azure-device-provisioning-service-with-self-signed-x-509-certificates-part-1/)

### Two Ways to Use This Series

#### Option 1: Set Up the Current Preview

Use our updated [step-by-step Azure CLI walkthrough in Part 3](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/)
to create the ADR namespace, a new IoT Hub, DPS, role assignments, and enrollment.
It retains the CLI method in this series, aligned with Microsoft's current
[Azure CLI deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).
Existing IoT Hubs have no upgrade path, and DPS is required for every IoT Hub
preview scenario.

After reviewing the current prerequisites and setup, use the repository's
device application instructions in [Part 5](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5/).

#### Option 2: Step-by-Step Tutorial (This Series)

Follow along post-by-post to understand **why** and **how** everything works. You'll learn:

-   DPS concepts and architecture
-   Azure resource setup
-   X.509 certificate workflows
-   .NET implementation details

### Prerequisites

-   **Azure Subscription** ([free trial available](https://azure.microsoft.com/free/))
-   **Azure CLI** with IoT extension ([install guide](https://learn.microsoft.com/cli/azure/install-azure-cli))
-   **PowerShell** 7+ ([install guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
-   **.NET 10** SDK ([download](https://dotnet.microsoft.com/download))
-   Basic C# and IoT concepts understanding

### Series Outline

1.  **A Primer on DPS** - Understanding Device Provisioning Service
2.  **Creating DPS and IoT Hub** - Azure resource setup
3.  **X.509 Certificate Hierarchy** - Certificate workflows
4.  **DPS Configuration & Enrollment** - Connecting devices
5.  **Building the Device Application** - .NET implementation
6.  **ADR Integration** - Device Registry features
7.  **Testing & Troubleshooting** - Production readiness

### Why This Series?

Most IoT tutorials use **symmetric keys** or **manual X.509 certificate generation**. This series shows you:

-   How to use Microsoft's **new certificate management** features
-   How to build a **custom DPS framework** to access preview APIs
-   How to integrate **ADR** for advanced device management
-   How to apply security-minded IoT patterns while accounting for preview limitations

### Next Steps

In the following sections, we'll:

1.  Learn about DPS with a Primer
2.  Create DPS, IoT Hub, and ADR instances
3.  Configure credential policies for certificate issuance
4.  Set up enrollment groups (symmetric key and X.509)
5.  Build a .NET device application with custom DPS framework
6.  Implement CSR generation and certificate management
7.  Integrate with ADR for device management

[Next: A DPS Primer >](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2)
