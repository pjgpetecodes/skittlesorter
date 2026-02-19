# Blog Post Series: Building IoT Devices with Azure DPS, ADR, and Certificate Management

A comprehensive 7-part series covering the latest Azure IoT features including Device Provisioning Service (DPS), Azure Device Registry (ADR), and Microsoft-managed certificate issuance.

## Series Overview

This series teaches you how to build production-ready IoT devices using Azure's newest features (preview as of January 2026):
- 🆕 **Microsoft Certificate Management** - Dynamic certificate issuance during provisioning
- 🆕 **Azure Device Registry (ADR)** - Device identity and management layer
- 🆕 **DPS MQTT Protocol** - Direct access to preview API features
- 🆕 **CSR-Based Provisioning** - Devices generate keys and request certificates

## Blog Posts

1. **[Introduction](00-introduction.md)** - Series overview and prerequisites
2. **[A Primer on DPS](01-primer-on-dps.md)** - Understanding Device Provisioning Service and new features
3. **[Creating Azure Resources](02-creating-azure-resources.md)** - Setting up DPS, IoT Hub, and ADR
4. **[Understanding X.509 and CSR Workflows](03-x509-and-csr-workflows.md)** - Certificate concepts and workflows
5. **[Configuring Enrollment Groups](04-configuring-enrollment-groups.md)** - DPS enrollment configuration
6. **[Building the Custom DPS Framework](05-building-dps-framework.md)** - MQTT protocol implementation
7. **[Building the Device Application](06-building-device-application.md)** - Complete .NET implementation
8. **[ADR Integration and Testing](07-adr-integration-testing.md)** - Device management and troubleshooting

## Quick Start vs Step-by-Step

### Option 1: Quick Start (Clone and Run)
```powershell
git clone https://github.com/yourusername/skittlesorter.git
cd skittlesorter
# Follow main README.md for setup
```

### Option 2: Step-by-Step Tutorial
Follow the blog posts in order to understand every component and decision.

## What You'll Build

By the end of this series, you'll have:
- ✅ Azure infrastructure (DPS, IoT Hub, ADR)
- ✅ Custom DPS framework with preview API support
- ✅ Complete .NET device application
- ✅ Certificate-based authentication
- ✅ Device telemetry and twin support
- ✅ ADR integration for device management

## Prerequisites

- **Azure Subscription** ([free trial available](https://azure.microsoft.com/free/))
- **Azure CLI** with IoT extension ([install guide](https://learn.microsoft.com/cli/azure/install-azure-cli))
- **PowerShell** 7+ ([install guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
- **.NET 10** SDK ([download](https://dotnet.microsoft.com/download))
- Basic C# and IoT concepts

## Key Technologies

- **Azure Device Provisioning Service (DPS)** - Zero-touch device provisioning
- **Azure Device Registry (ADR)** - Device identity management
- **Azure IoT Hub** - Cloud-to-device and device-to-cloud messaging
- **X.509 Certificates** - Secure authentication
- **MQTT Protocol** - Lightweight messaging
- **.NET 10** - Application framework
- **C#** - Programming language

## Related Documentation

- [Azure Setup Guide](../docs/Azure-Setup.md)
- [DPS Provisioning Guide](../docs/DPS-Provisioning.md)
- [ADR Integration Guide](../docs/ADR-Integration.md)
- [Security Best Practices](../docs/Security.md)
- [Troubleshooting Guide](../docs/Troubleshooting.md)

## Project Structure

```
skittlesorter/
├── blog/                       # Blog post series (you are here)
├── docs/                       # Technical documentation
├── src/                        # Device application code
├── AzureDpsFramework/          # Custom DPS framework
├── scripts/                    # PowerShell automation scripts
└── appsettings.json           # Configuration
```

## Contributing

Found an issue or have a suggestion? Please open an issue on GitHub!

## License

MIT License - See [LICENSE](../LICENSE) for details

---

**Start Reading:** [Introduction →](00-introduction.md)
