# Introduction: Building an IoT Device with Azure DPS, ADR, and Certificate Management

Welcome to this comprehensive blog series on building a production-ready IoT device using Azure's latest Device Provisioning Service (DPS) features, Azure Device Registry (ADR), and Microsoft-managed certificate infrastructure.

## What You'll Build

By the end of this series, you'll have:
- ✅ A complete IoT device application (Skittle Sorter)
- ✅ Automated device provisioning with DPS
- ✅ Microsoft-managed X.509 certificate issuance
- ✅ Azure Device Registry integration
- ✅ Production-ready security practices
- ✅ Full automation scripts

## What's New in 2025-2026

This series focuses on **preview features** released in late 2025/early 2026:

### 🆕 Microsoft Certificate Management
- DPS can now **issue certificates dynamically** during provisioning
- No need to manage your own Certificate Authority (CA)
- Devices submit Certificate Signing Requests (CSR)
- Microsoft signs and returns certificates automatically

### 🆕 Azure Device Registry (ADR)
- New device identity and management layer
- Decoupled from IoT Hub for better scalability
- Device attributes, tags, and metadata management
- Credential policies for automated certificate lifecycle

### 🆕 DPS MQTT Protocol Support
- Direct MQTT communication with DPS
- Access to preview API features (`2025-07-01-preview`)
- Lower overhead than HTTPS/AMQP

## Two Ways to Use This Series

### Option 1: Quick Start (Clone and Run)
```powershell
git clone https://github.com/yourusername/skittlesorter.git
cd skittlesorter
# Follow README.md for one-command setup
```

### Option 2: Step-by-Step Tutorial (This Series)
Follow along post-by-post to understand **why** and **how** everything works. You'll learn:
- DPS concepts and architecture
- Azure resource setup
- X.509 certificate workflows
- .NET implementation details
- ADR integration patterns

## Prerequisites

- **Azure Subscription** ([free trial available](https://azure.microsoft.com/free/))
- **Azure CLI** with IoT extension ([install guide](https://learn.microsoft.com/cli/azure/install-azure-cli))
- **PowerShell** 7+ ([install guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
- **.NET 10** SDK ([download](https://dotnet.microsoft.com/download))
- Basic C# and IoT concepts understanding

## Series Outline

1. **[A Primer on DPS](01-primer-on-dps.md)** - Understanding Device Provisioning Service
2. **[Creating DPS and IoT Hub](02-creating-dps-and-iot-hub.md)** - Azure resource setup
3. **[X.509 Certificate Hierarchy](03-x509-certificate-hierarchy.md)** - Certificate workflows
4. **[DPS Configuration & Enrollment](04-dps-configuration-enrollment.md)** - Connecting devices
5. **[Building the Device Application](05-building-device-application.md)** - .NET implementation
6. **[ADR Integration](06-adr-integration.md)** - Device Registry features
7. **[Testing & Troubleshooting](07-testing-troubleshooting.md)** - Production readiness

## Why This Series?

Most IoT tutorials use **symmetric keys** or **manual X.509 certificate generation**. This series shows you:
- How to use Microsoft's **new certificate management** features
- How to build a **custom DPS framework** to access preview APIs
- How to integrate **ADR** for advanced device management
- How to create **production-ready** IoT solutions

Let's get started! 🚀

---

[Next: A Primer on DPS →](01-primer-on-dps.md)
