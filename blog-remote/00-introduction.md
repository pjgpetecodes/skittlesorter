## **Introduction**

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

### What's New in 2025-2026

This series focuses on **preview features** (starting November 2025):

In November 2025, Microsoft announced the public preview of Azure IoT Hub integration with Azure Device Registry (ADR), bringing IoT devices under the Azure management plane with ARM resource representation.

ADR includes an optional **certificate management** feature that provides Microsoft-backed X.509 PKI, eliminating the need for custom certificate infrastructure.

ADR now serves as a unified control plane for managing both IoT Hub devices and Azure IoT Operations assets, with policy-driven certificate lifecycle management that automates issuance and renewal at scale.

### Working with Preview APIs

As of the time of writing (1st February 2026), these are **preview features** and the official Microsoft C# SDK doesn't yet support the new DPS and certificate management capabilities.

However, I've built a **custom DPS framework** that communicates directly with the preview APIs using **MQTT protocol**.

It's worth noting that Microsoft does provide SDK support for **Python** and **C/C++**, but C# developers need to wait for official support or use custom implementations like mine in the meantime.

#### 🆕 Azure IoT Hub + Azure Device Registry (ADR) Integration (Preview)

-   IoT Hub integrates with ADR to provide a **unified device registry** across IoT Hub and IoT Operations
-   ADR namespaces enable centralized device metadata and identity management
-   ADR integration is required for the latest provisioning and certificate management features

#### 🆕 Microsoft-Backed X.509 Certificate Management (Preview)

-   ADR offers **certificate management** using Microsoft-managed PKI
-   Issues and renews **operational X.509 certificates** for device authentication to IoT Hub
-   Certificates are chained to Microsoft-managed Certificate Authorities (CAs)
-   Devices onboard through DPS, then receive operational certs via policy-driven certificate issuance
-   **DPS is required** for provisioning (DPS must be linked and used for all preview scenarios)

> ⚠️ **Preview Notice:** ADR integration and Microsoft-backed certificate management are in public preview and **not recommended for production workloads**.

#### Supported Regions (Preview)

-   East US
-   East US 2
-   West US
-   West US 2
-   West Europe
-   North Europe

### Official Docs & Announcements

-   Azure IoT Hub + ADR preview announcement: [https://techcommunity.microsoft.com/blog/iotblog/azure-iot-hub-with-adr-preview-extending-azure-capabilities-and-certificate-mana/4465265](https://techcommunity.microsoft.com/blog/iotblog/azure-iot-hub-with-adr-preview-extending-azure-capabilities-and-certificate-mana/4465265)
-   Azure IoT Hub “What’s New”: [https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new)

### Learning the Traditional DPS Approach

If you're interested in learning the traditional DPS workflow with self-signed X.509 certificates (without the new ADR and certificate management features), I've written a series of blog posts covering that approach:

[Using Azure Device Provisioning Service with Self-Signed X.509 Certificates](https://www.petecodes.co.uk/using-azure-device-provisioning-service-with-self-signed-x-509-certificates-part-1/)

### Two Ways to Use This Series

#### Option 1: Quick Start (Clone and Run)

```powershell
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter/scripts
$env:Path = "C:\Program Files\OpenSSL-Win64\bin;$env:Path"

# Full ADR + X.509 setup (recommended)
.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "eastus" `
  -IoTHubName "my-hub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adr-001" `
  -UserIdentity "my-uami"
```

Or, if you have the Azure resources provisioned already, you can also just run the X.509 Setup script;

```json
.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg"
```

Once the script completes, update your device settings:

```json
{
  "IoTHub": {
    "DpsProvisioning": {
      "IdScope": "&lt;Your ID Scope>",  // From script output
      "RegistrationId": "skittlesorter",
      "AttestationMethod": "X509",
      "AttestationCertPath": "C:\\repos\\skittlesorter\\scripts\\certs\\device\\device.pem",
      "AttestationKeyPath": "C:\\repos\\skittlesorter\\scripts\\certs\\device\\device.key",
      "AttestationCertChainPath": "C:\\repos\\skittlesorter\\scripts\\certs\\ca\\chain.pem"
    }
  },
  "Adr": {
    "Enabled": true,
    "SubscriptionId": "&lt;Your Subscription ID>",
    "ResourceGroupName": "pjgiothubdemo003",
    "NamespaceName": "pjgadrnamespace003"
  }
}
```

### Run the Device Application

Once you've run one of the above two commands, you'll need to update your appsettings.json file with the results printed out at the end of the script execution.

You can then run the skittle sorter with;

```powershell
cd ..      # You should be in the skittlesorter directory
dotnet run --project src/skittlesorter.csproj
```

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
-   How to apply **production-minded** IoT patterns that carry over to trusted, CA-issued certificates

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
