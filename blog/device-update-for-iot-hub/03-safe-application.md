# Part 3: Stop at a safe cycle boundary, then replace the application

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. Draft: physical servo shutdown and systemd behaviour still require Pi evidence.

An application update must not kill a servo movement halfway through a drop. `src\Program.cs` now handles console cancellation and Linux `SIGTERM` cooperatively: stop admitting cycles, finish the current cycle, then stop PWM and dispose connections. The service must not convert an expired stop timeout into permission to force-kill hardware.

The actual running loop is in `Program.cs`, not the separate `SkittleSorterService` example. That distinction matters when adapting older code snippets.

## Stable paths

Keep application releases under `/opt/skittlesorter/releases`, selected through `/opt/skittlesorter/current`. Keep configuration under `/etc/skittlesorter` and state/readiness under `/var/lib/skittlesorter`. Certificates and credentials stay outside the release directories.

The service invokes:

```bash
/opt/skittlesorter/current/skittlesorter \
  --config /etc/skittlesorter/appsettings.json \
  --ready-file /var/lib/skittlesorter/ready.json
```

The app also supports `--version` and a non-moving `--self-test --config PATH`. The self-test checks the configuration document without accessing GPIO or the network; it is not proof of working hardware. Runtime readiness is written atomically after initialization, with the running version, process ID and timestamp, and removed on orderly exit.

## Installation is a transaction

The trusted local installer validates the release archive and metadata before stopping the sorter. Unsafe archive paths, links, architecture mismatches and invalid releases must fail without changing the active app. It serializes installation, stages a versioned directory, records recovery state, stops the service, switches the active release, starts the service and verifies expected readiness.

If startup fails, it restores the prior release and checks that recovery, rather than claiming that a running PID proves the target is healthy. If recovery cannot restore a healthy release, stop for operator attention.

Read `deploy\updates\README.md` and the [runbook](../../docs/Application-Updates.md) for the actual bootstrap and recovery commands. Install the systemd units from `deploy\systemd` only after reviewing their user, GPIO/I2C groups, paths and timeout. The sorter and network updater should not need unrestricted root business logic; the local installation boundary has the privileges required to switch releases and control the service.

Test an empty, unpowered or mocked sorter first. Before a live update, capture the current symlink, version and protected-config checksum. After both upgrade and rollback, confirm configuration and keys are unchanged.

[Previous](02-lab-and-pi.md) | [Next: packaging](04-packaging-and-import.md)
