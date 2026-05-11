# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build / run / publish

```powershell
# Build the whole solution. Bin/obj land in .build/ (centralised).
dotnet build Claudelk.slnx

# Run a CLI command in development (slow: ~5s dotnet run overhead).
dotnet run --project src/Claudelk.Cli -- <subcommand> [args]

# Publish the production exe used by Claude Code hooks.
dotnet publish src/Claudelk.Cli -c Release
# Default output: src\Claudelk.Cli\bin\Release\<tfm>\publish\
# The committed csproj does NOT hardcode a PublishDir. Per-developer install
# path lives in src\Claudelk.Cli\Claudelk.Cli.csproj.user (gitignored *.user).
# Set <PublishDir> there to redirect publish at whatever path your Claude Code
# hooks invoke. ~1.6s cold per command.
```

CLI subcommands: `scan [--debug]`, `pair <id>`, `on`, `off`, `color <#RRGGBB>`, `blink <#RRGGBB> [pulses] [ms] [--end <#RRGGBB>]`, `brightness 0-100`, `speed 0-100`, `effect 0x80-0x9f`, `temp 0-100`. All commands accept `--device <id>` to override the saved default.

## Tests

```powershell
# Build + run all NUnit tests (Claudelk.Core.Tests).
dotnet test Claudelk.slnx
```

`tests/Claudelk.Core.Tests/` (NUnit 4 on Microsoft.Testing.Platform) covers:

- The pure-protocol byte builders in `ElkBledomProtocol`.
- `UserConfig` JSON round-trip + missing/malformed-file behaviour.
- `ElkBledomScanner` name-prefix matching and host-availability propagation, via a `FakeBluetoothHost`.
- `ElkBledomDevice` command byte streams and `ConnectByIdAsync` resolution (paired-list fast path → scan fallback → not-found error), via a `FakeBluetoothDevice`.

The BLE abstraction layer (`IBluetoothHost` / `IBluetoothDevice` + the `InTheHand` adapters under `src/Claudelk.Core/Bluetooth/InTheHand/`) exists so the protocol-level code is testable; the InTheHand adapters themselves still need a real ELK-BLEDOM strip to smoke-test.

The test project sets `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` so `dotnet test` invokes MTP rather than legacy VSTest. The output format is `Run tests:` / `Tests succeeded:` instead of VSTest's `Test run for...` / `Passed! - Failed: 0...`.

## Build infrastructure (repo-root files)

The csprojs are intentionally minimal — shared concerns live in repo-root MSBuild files. Edit these instead of duplicating settings per-project.

- **`global.json`** — pins the .NET SDK to 10.0.x.
- **`Directory.Build.props`** — `TargetFramework` (Windows-specific TFM is mandatory, see gotcha #1 below), `Nullable`, `LangVersion`, **strict** analyzer settings (`TreatWarningsAsErrors`, `WarningLevel=9999`, `AnalysisMode=AllEnabledByDefault`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`), `NoWarn=NU1903`, `BaseOutputPath`/`BaseIntermediateOutputPath` → `.build/`, and analyzer `PackageReference`s for **Meziantou.Analyzer** + **AsyncFixer**.
- **`Directory.Packages.props`** — central package management; bare `<PackageReference Include="X" />` in csprojs resolves versions from here.
- **`nuget.config`** — `nuget.org` only with package-source mapping locked to it.
- **`.editorconfig`** — formatting (4-space, CRLF, UTF-8) plus a small, audited set of analyzer-rule silences: IDE0008/0011/0021/0022/0046/0300 (pure style), `MA0004` (ConfigureAwait — irrelevant in a console app), `MA0136` (raw-string EOL nag on help text), `MA0165` (experimental rule even Meziantou disables by default). All other rules either fire as errors or were fixed in code (the culture-aware `int.TryParse` / `string.Create` patterns in `Dispatcher` are deliberate, not workarounds). The same file also forbids top-level statements and enforces a blank line after every code block (`IDE2003`).

When adding a new project: just create the csproj — TFM, language version, strictness, and analyzers come from the repo-root files automatically.

## Hooks live in `~/.claude/settings.json`

The Claude Code integration runs the published exe with `"async": true` and `"shell": "powershell"` on lifecycle events. The hook command path in `~/.claude/settings.json` must match the `<PublishDir>` configured in `Claudelk.Cli.csproj.user`. The repo does not prescribe a specific colour-per-event scheme — see the "Claude Code integration" section in `README.md` for example configurations to crib from.

## Architecture

Three layers, each in its own folder under `src/Claudelk.Core/`:

- **`Protocol/ElkBledomProtocol.cs`** — pure functions that produce 9-byte command packets (`7e ... ef`). No I/O. This is the only place protocol byte layout lives; reuse it rather than re-encoding bytes elsewhere.
- **`Bluetooth/`** — `ElkBledomScanner` (BLE discovery) and `ElkBledomDevice` (GATT connect + per-command write helpers). Both wrap `InTheHand.BluetoothLE` (32feet.NET v4.x).
- **`Configuration/UserConfig.cs`** — persists the last paired device id to `%APPDATA%\Claudelk\config.json`.

`src/Claudelk.Cli/` is thin: `Program.cs` delegates to `Dispatcher.RunAsync`, which parses args and calls `ElkBledomDevice` methods. Device resolution flows through `ResolveDeviceAsync` → `ElkBledomDevice.ConnectByIdAsync`, which tries `Bluetooth.GetPairedDevicesAsync()` first (fast path, no scan) and falls back to a 3s advertisement scan.

The future Claude integration *daemon* (phase 3 in `README.md`) is not yet built; it would live in a third project depending on `Claudelk.Core`.

## BLE-stack gotchas (these caused real bugs in this repo)

1. **Both csprojs must target `net10.0-windows10.0.19041.0`.** With plain `net10.0`, `InTheHand.BluetoothLE` selects its Linux/BlueZ backend and every scan fails at runtime with `No path specified for UNIX transport`.

2. **The `InTheHand.Bluetooth` namespace shadows `Claudelk.Core.Bluetooth`.** When calling the static `Bluetooth.ScanForDevicesAsync` / `GetAvailabilityAsync` / `GetPairedDevicesAsync`, alias it: `using IhBluetooth = InTheHand.Bluetooth.Bluetooth;`.

3. **The Web-Bluetooth-spec property is `AcceptAllDevices`, not `AcceptAllAdvertisements`.** `RequestDeviceOptions` also has a `Timeout` property — use it; don't rely on defaults.

4. **Name-based BLE scan filters are unreliable on Windows** (the name is in the scan response, not the advertisement). `ElkBledomScanner` scans with `AcceptAllDevices = true` and filters in C# against known name prefixes (`ELK-BLEDOM`, `ELK-BLE`, `LEDBLE`, `MELK`, …).

5. **`Tmds.DBus` `NU1903` advisory** is a Linux-only transitive dep pulled in by `InTheHand.BluetoothLE`. Suppressed via `<NoWarn>$(NoWarn);NU1903</NoWarn>` in `Directory.Build.props` — don't remove that line on Windows builds.

## Performance notes for the CLI

Per one-shot command: ~0.3s .NET startup + ~1s cold GATT connect/service discovery + ~30ms write ≈ **~1.6s floor on the published exe**. Use the published exe (not `dotnet run`) when latency matters. To go below ~1.6s, the daemon (phase 3) is the only path — it would hold the GATT connection open across commands.

`Dispatcher.ResolveDeviceAsync` skips advertisement scanning entirely when the device is in `GetPairedDevicesAsync()` — keep this fast path. Slowdowns usually mean the device fell out of Windows' paired list (re-pair via Settings or rerun `claudelk pair <id>`).

## Attribution

This codebase ports ideas/protocol bytes from upstream projects. See `NOTICE.md` for the full list — when modifying the protocol module or adding a Claude integration layer, keep the attribution lines in the file headers.
