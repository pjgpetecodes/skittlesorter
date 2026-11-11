# Part 6: Demonstrate recovery without claiming production readiness

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft: real service and physical-Pi failures remain unverified.

The custom updater should fail loudly at the correct boundary, rather than producing a success-shaped fallback. Exercise it with deterministic local tests first, then obtain independent service/Pi evidence.

| Exercise | Evidence needed |
|---|---|
| Invalid JWS, disabled/unknown key or signed digest mismatch | Rejection before payload installation |
| Payload corruption or HTTPS downgrade | Bounded download failure; current app unchanged |
| Unsupported handler/reference/multistep content | Refusal before invoking the local installer |
| Repeated completed workflow | No second installation; correct installed identity |
| Report disconnect/restart | Terminal result remains durable and is replayed |
| Report conflict | Visible reconciliation requirement, not false acknowledgement |
| New release not ready | Verified rollback; failed target not recorded as installed |
| Replacement during a transaction | No interruption of the active local install |
| Repeated failed workflow | Persisted backoff and jitter; no tight polling loop |

Use mock hardware for interruption exercises. A root-key fixture signed by a generated test key demonstrates cryptographic validation logic; it does not prove a Microsoft-signed service offer was accepted. Likewise a simulated transport cannot demonstrate concurrent real Hub telemetry and serialized DPS sessions.

For unresolved local recovery, stop and preserve diagnostics. Do not delete the workflow state, transaction journal or old release just to make a job appear green. Preserve configuration and key checksums throughout.

## Adopting an official .NET implementation later

When official .NET support arrives, first read its actual software-update capability matrix, current package version and migration contract. Do not predict class names or assume today's custom state format is compatible.

Keep the application payload and trusted local installation boundary. Replace protocol, trust/state machinery and host integration explicitly, then repeat authentication, no-update, signed offer, rollback, duplicate and report-replay proofs. Avoid running two update clients simultaneously as a “migration test.”

**What's changed in provisioning?** The sorter now pins
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** and uses its unified
`ConnectionClient`/`TelemetryClient`. `AzureDpsFramework` retains configuration,
durable certificate/CSR persistence and ADR ARM helpers. The sorter calls the SDK
directly; its custom provisioning protocol is removed. The SDK still doesn't implement DPS
software updates, so `AzureSoftwareUpdatesFramework` keeps its own update protocol
and MQTTnet 4 while the sorter uses MQTTnet 5. Optional SDK renewal APIs do not
mean automatic certificate renewal has been implemented in the app.

Before publishing either series, replace outstanding validation notes with real, redacted observations: target OS/architecture, exact agent/client versions, actual service/API availability, successful update, rejected tampering, healthy rollback and restart evidence. Until then these are implementation-backed drafts, not a production deployment guide.

[Previous](05-groups-and-jobs.md) | [Series index](README.md)
