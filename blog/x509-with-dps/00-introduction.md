# Using Self-Signed X.509 Certificates with Azure IoT DPS

[Next: A Primer on DPS →](01-primer-on-dps.md)

---

## Why This Guide?

For testing, demos, and development reasons, it can be useful (and a fair amount cheaper!) to use self-signed X.509 certificates instead of purchasing them from a certificate authority.

However, setting this up involves multiple steps and has several pitfalls. This guide will help you navigate through the process and get your DPS setup and IoT devices provisioning properly.

## What You'll Learn

This is a comprehensive, hands-on guide to:

1. **Understanding DPS** - How Device Provisioning Service works and when to use it
2. **Creating Azure Resources** - Setting up DPS and IoT Hub in your subscription
3. **Building a Certificate Hierarchy** - Generating a 3-tier PKI (root, intermediate, device certs)
4. **Uploading & Verifying Certificates** - Getting your self-signed certs into Azure
5. **Configuring Enrollment** - Setting up DPS to trust your certificates
6. **Device Implementation** - Writing device code to authenticate and provision
7. **Understanding the Provisioning Flow** - Seeing exactly what happens end-to-end

## Prerequisites

- Azure subscription with permissions to create resources
- Azure CLI installed and configured
- Azure CLI IoT extension (`az extension add --name azure-iot`)
- OpenSSL installed (for certificate generation)
- Basic understanding of IoT concepts and X.509 certificates
- Comfort with PowerShell or bash scripting

## What This Guide Covers

✅ **What's Included:**
- Complete self-signed certificate hierarchy setup
- Azure resource creation and configuration
- Proof-of-possession certificate verification
- DPS enrollment group creation
- Device provisioning implementation
- Troubleshooting and common issues

❌ **What's Out of Scope:**
- Enterprise PKI solutions (we're doing self-signed for dev/test)
- Hardware security modules (HSM) integration
- Production certificate management best practices
- Azure AD/Entra integration

## How to Use This Guide

Each section is self-contained but builds on the previous ones. You can:

- **Read straight through** - Recommended for first-time learners
- **Jump to a section** - If you already know parts, use the navigation links
- **Reference specific topics** - Use the table of contents below

## Structure

1. **[01 - Primer on DPS](01-primer-on-dps.md)** - Conceptual overview of Device Provisioning Service
2. **[02 - Creating DPS and IoT Hub](02-creating-dps-and-iot-hub.md)** - Azure resource setup
3. **[03 - Creating X.509 Certificates](03-creating-x509-certificates.md)** - Self-signed certificate generation
4. **[04 - Uploading Certificates to DPS](04-uploading-certificates-to-dps.md)** - Getting certs into Azure
5. **[05 - Verifying X.509 Certificates](05-verifying-x509-certificates.md)** - Proof-of-possession process
6. **[06 - Creating Enrollment Groups](06-creating-enrollment-groups.md)** - DPS configuration for device authorization
7. **[07 - Setting up a Simulated Device](07-setting-up-simulated-device.md)** - Device code implementation
8. **[08 - Provisioning Flow](08-provisioning-flow.md)** - End-to-end walkthrough

## Important Disclaimer

**Self-signed certificates are NOT suitable for production.** They lack:
- Certificate chain validation from a trusted authority
- Revocation mechanisms
- Centralized management and audit trails
- Industry compliance (FIPS, security standards)

Use this guide for:
- Development and testing
- Proof-of-concept projects
- Demo environments
- Understanding how X.509 provisioning works

For production deployments, use certificates from a trusted certificate authority or consider using other attestation methods (symmetric keys, TPM).

## Let's Get Started!

Ready to set up X.509 certificate-based provisioning? Head to the next section to learn how DPS works.

---

[Next: A Primer on DPS →](01-primer-on-dps.md)
