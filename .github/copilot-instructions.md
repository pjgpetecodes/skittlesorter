# Copilot instructions

## Build and test

Use the .NET 10 SDK. From the repository root:

```powershell
dotnet build .\skittlesorter.sln
dotnet test .\skittlesorter.sln
python -m unittest discover -s .\tests\updates -v
python -m unittest discover -s .\tests\installer -v
```

Run one .NET test by fully qualified name, for example:

```powershell
dotnet test .\tests\SkittleSorter.Tests\SkittleSorter.Tests.csproj --filter "FullyQualifiedName~SkittleSorter.Tests.UpdateReadinessTests.StandardProfileUsesDeviceKeyWithoutRequestingCertificate"
```

For focused runs, the test projects are `tests\SkittleSorter.Tests` and `tests\AzureSoftwareUpdatesFramework.Tests`. Python tests use `unittest`; the two discovery commands above cover update tooling and installer tests. CI also publishes a Linux app build and runs a process-lifecycle test against it.

## Architecture

- `skittlesorter.csproj` is the .NET 10 hardware/IoT executable. `src\Program.cs` loads configuration, initializes mock or physical sensor/servos, provisions/connects through `AzureDpsFramework`, and runs the pick-detect-sort-drop loop with optional IoT Hub telemetry.
- `AzureDpsFramework` is a thin configuration, certificate/CSR persistence, ADR ARM, and unified-session adapter library. The sorter pins `Microsoft.Azure.Iot.Device` 2.0.0-preview4 and uses its unified `ConnectionClient` and `TelemetryClient`; do not reintroduce custom DPS provisioning/security/SAS-token transports. Preserve the existing Hub topology and let the SDK discover its assigned endpoint/connection profile through DPS.
- `AzureSoftwareUpdatesFramework` is a separate preview DPS software-updates client. `tools\SkittleSorter.Updater` runs it independently of the sorter; both update demonstrations share the transactional installer under `scripts\updates` / `deploy\updates`. The GA Device Update agent and ADR preview updater use separate cloud resources; do not combine their controllers on the same Hub or run both on one Pi.
- The solution includes the app, both frameworks, the updater, and test projects. The main app compiles `src\**\*.cs` explicitly, so application code belongs under `src`.
- See `docs\Architecture.md` and `docs\Application-Updates.md` for the system boundaries and update runbooks; see each framework README for its protocol/package details.

## Repository-specific conventions

- Runtime configuration is selected with `--config`; plain `dotnet run` uses root `appsettings.json`. Use the root `appsettings.*.template.json` files as safe examples; `appsettings.device-update.template.json` remains under `src\configuration`. Keep credentials, private keys, and deployment state out of source control and release artifacts.
- Hardware supports mock and physical implementations. Keep configuration-driven hardware behavior testable without a Raspberry Pi; use `--self-test --config PATH` for configuration-only validation.
- The app supports both standard DPS provisioning and preview operational-certificate issuance. Check the selected profile and preserve its compatibility defaults when changing provisioning behavior.
- For remote WordPress articles, edit the matching Markdown under `blog-remote`, regenerate/sync its paired HTML locally, and review the push preview before any explicit `--apply`. Do not replace the separate drafts under `blog` or publish automatically. Preserve imported Gutenberg `wordpress-block` fences unless intentionally changing those blocks.
- Package `AzureDpsFramework` through the workflows in `.github\workflows`; versioned publishing is tag-based (`v<version>`).
