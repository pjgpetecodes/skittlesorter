# Part 5: Prove the update on one sorter

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. Draft procedure: no successful Azure/Pi rollout is claimed.

Start with a healthy 1.0.0 baseline installed locally. Check `current`, the app's `--version`, readiness, service logs and sorter telemetry. Confirm the standard update agent is connected **as the `adu` module**, while the sorter remains connected as its parent device.

In Device Update, inspect the group created from the module twin's `ADUGroup` tag. Check member identities, device classes and architecture compatibility. This demonstration targets one Pi; it is not evidence of a multi-device canary rollout.

Only after import is deployable and the resolved group contains the intended module, invoke:

```powershell
.\scripts\updates\device-update\deploy-update.ps1 `
  -Subscription "<subscription>" -Account "<GA-account>" -Instance "<GA-instance>" `
  -Group skittlesorter-pi4 -Deployment sorter-110 `
  -Runtime linux-arm64 -Version 1.1.0
```

That is a preview. Review the selected group and add `-Apply` to submit the deployment. The wrapper requires HTTPS downloads and then reads cloud deployment status. A cloud status query returning successfully is not the same as the device having completed installation.

## Evidence to collect

Record the import/update identity, group membership, module identity, start time, agent result and device-level deployment status. On the Pi collect:

```bash
readlink -f /opt/skittlesorter/current
/opt/skittlesorter/current/skittlesorter --version
cat /var/lib/skittlesorter/ready.json
systemctl status skittlesorter --no-pager
journalctl -u skittlesorter --since "10 minutes ago" --no-pager
```

The active target should identify 1.1.0, readiness should describe the actual running process, and new telemetry should contain `appVersion`, `sessionTotal` and `sessionCounts`. Observe actual sorter operation with known colours; compare total to the sum of per-colour counts.

Save redacted evidence in the operator's lab records, not fabricated screenshots in this draft. Remove connection strings, SAS URLs and private keys from captures. Reboot or restart deliberately and confirm a healthy selected release returns; new session counts start from zero.

The next article exercises failure handling. Do not treat this happy-path checklist as permission to skip rollback testing.

[Previous](04-packaging-and-import.md) | [Next: recovery](06-recovery.md)
