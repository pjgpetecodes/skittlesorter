# Part 4: Package the session-count feature and import real hashes

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. Draft: payloads can be built locally; service import remains unverified.

Both releases come from the same C# application. The build flag removes session counting from the baseline while keeping version/lifecycle support:

```powershell
.\scripts\updates\common\publish-release.ps1 -Runtime linux-arm64 -Version 1.0.0 -SessionCounts $false
.\scripts\updates\common\publish-release.ps1 -Runtime linux-arm64 -Version 1.1.0 -SessionCounts $true
```

Substitute `linux-arm` only if the Pi userland requires it. These are self-contained .NET publishes, not OS images. Use fresh output directories for rebuilds; the script refuses stale publish output. The archive has a Linux executable and generated `release.json` containing version, runtime and feature list. The packager rejects application configuration, certificate/private-key files and symbolic links.

In the feature build, a completed red/red/green sequence produces total 3 and per-colour counts red 2, green 1. Restarting the process starts a new session. We deliberately avoid changing the meaning of this data into durable production totals.

## Generate, don't hand-type, payload metadata

```powershell
python .\scripts\updates\common\new-import-manifest.py `
  --channel ga --runtime linux-arm64 --version 1.1.0 `
  --payload .\artifacts\updates\skittlesorter-1.1.0-linux-arm64.tar.gz `
  --output .\artifacts\updates\ga-1.1.0.importmanifest.json
```

The schema-5 manifest hashes actual files using base64 SHA-256 and records byte sizes. The GA manifest imports both the archive and `install-sorter.py`, selecting `microsoft/script:1`. Its compatibility includes architecture so an ARM32 device cannot accidentally receive an ARM64 app.

The adapter understands the standard handler's appended action/work-folder/result-file arguments. It invokes only the fixed local installer; phase results are written as ADUC result JSON. Its installed check compares version/runtime metadata. The local installer performs the stronger running-version/readiness check during installation.

## Stage and import

Run `scripts\updates\device-update\import-update.ps1` with subscription, account, instance, storage and manifest. Preview is the default; `-Apply` invokes `az iot du update stage --then-import true`. All listed files must be alongside the manifest.

The CLI staging workflow uses storage-account-key access and generates temporary read SAS URLs. Protect logs and do not add `--debug` to demonstrations containing credentials. The wrapper suppresses routine import output; inspect the update in the portal or with `az iot du update show` and verify it is deployable before proceeding. An accepted import is not yet a deployment.

[Previous](03-safe-application.md) | [Next: deployment evidence](05-deployment.md)

Source: [script handler contract, agent 1.4.0](https://github.com/Azure/iot-hub-device-update/blob/1.4.0/src/extensions/step_handlers/script_handler/README.md).
