# Updating the Skittle Sorter with Device Update for IoT Hub

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. These are local drafts; Azure deployment and physical Raspberry Pi validation are outstanding.

This is a separate application-update series, not another instalment of the unfinished ADR certificate-management series. The same C# sorter moves from a version-visible baseline to a session-count feature release. The standard Linux Device Update agent handles the GA cloud protocol; a narrowly scoped local installer handles safe replacement and rollback.

1. [Architecture and boundaries](01-architecture.md)
2. [An independent Azure lab and Pi preparation](02-lab-and-pi.md)
3. [A safely updateable sorter](03-safe-application.md)
4. [Packaging and importing the feature](04-packaging-and-import.md)
5. [Deploying and collecting evidence](05-deployment.md)
6. [Failures, recovery and repeatable demonstrations](06-recovery.md)

Follow the [ADR Software Updates series](../adr-software-updates/README.md) only after proving this path. ADR requires a different Hub and client. A successful build or mocked installer test is not a successful OTA deployment.

Source and operational details: [application-update runbook](../../docs/Application-Updates.md). Markdown here is the source for adjacent WordPress HTML; matching draft copies under `blog` are generated locally. Nothing in this series publishes to WordPress.
