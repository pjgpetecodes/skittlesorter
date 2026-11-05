# Part 4: Verify trust before trusting a payload hash

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft: trusted service fixtures and real Pi recovery remain validation gates.

A SHA-256 value in untrusted JSON only proves that two attacker-controlled values agree. Before downloading or installing anything, verify the manifest's signed digest through the published RS256/JWS signing chain and an independently trusted root-key baseline.

Keep the trust baseline out of band. A root-key URL returned by the service is input, not permission to trust a new root. Local generated test keys prove verifier behaviour; they are not Microsoft service trust anchors. Reject unsupported algorithms, unknown or disabled keys, malformed signatures and mismatched manifest digests.

The first custom client intentionally fails closed rather than bootstrapping trust from an arbitrary network response. Review its README for supported key-baseline handling and limitations; root rotation requires an authenticated trust update, not a “disable verification” demonstration switch.

## Bound the download

After signature verification, validate the known application manifest shape. Require HTTPS, including any redirect destination, bounded download time/size and exact payload SHA-256/size. Never print SAS URLs in normal logs. Hash/signature failure must occur before calling the privileged installer.

The installer receives one verified archive and explicit expected version/runtime:

```bash
python3 /usr/lib/skittlesorter/installer.py install \
  --root /opt/skittlesorter \
  --payload /protected/staging/skittlesorter-1.1.0-linux-arm64.tar.gz \
  --version 1.1.0 --runtime linux-arm64
```

The unprivileged updater does not invoke that root CLI directly. It downloads into `/var/cache/skittlesorter-updates/incoming` and calls the fixed `sudo -n /usr/lib/skittlesorter/installer-helper install --payload BASENAME --version VERSION --runtime RUNTIME` boundary. The root-owned helper snapshots the file before invoking the fixed installer. Do not run the network client as root merely to make a demonstration work. Follow `deploy\updates\README.md` and `install-installer-helper.sh` for bootstrap and narrowly scoped sudo authorization.

## Two recovery boundaries

The local transaction journal resolves application staging, activation, readiness and rollback. The updater state resolves offered workflow, installed identity, retry schedule and reports owed to DPS. Neither replaces the other.

If local startup fails and the previous release is restored, retain the previous installed update ID. If installation succeeds but reporting disconnects, persist the successful result before retrying the report; do not reinstall just to generate another status message.

Each check/report uses a short-lived DPS session, with bounded reconnect attempts and unchanged owed-report replay. The updater disconnects and releases the shared provisioning lock before download, installation or polling sleep, allowing the sorter to reprovision on restart.

This initial client stops for journal/active-release reconciliation if restarted at an ambiguous installer boundary; it does not automatically infer that transaction's outcome. Corrective polling after report conflict remains an operator reconciliation gate. These are tracked demonstration limitations, not completed production recovery features.

The published failed-workflow policy uses a minimum one-hour retry, doubling toward 24 hours with jitter. Normal polling defaults to four hours. A bounded manual check is for an attended demonstration, not a permanent tight loop.

Job cancellation is service-side: stopping future offers does not guarantee aborting a payload already being installed. Never interrupt a physical drop in response to a cloud “cancel” label.

[Previous](03-custom-client.md) | [Next: groups and jobs](05-groups-and-jobs.md)
