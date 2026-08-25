# Rainmeter Codex Quota Monitor

[简体中文](README_zh-CN.md)

![Dynamic Glacier preview](assets/preview.png)

An unofficial, task-aware Rainmeter dashboard for viewing the quota reported by the locally installed Codex application.

## Features

- Refreshes immediately when a Codex task starts, then every 20 seconds while a task remains active.
- Performs one final refresh on completion and enters a static `SILENT` state with no remote polling.
- Adds a bottom-row `QUIET / RESUME` control beside `TOP OFF / TOP ON` to pause polling and breathing during an active task.
- Shows remaining quota, reset countdown, restore date and time, plan, and active-query countdown.
- Uses a full-edge breathing animation only while active or syncing.
- Includes a persistent `TOP OFF / TOP ON` switch for normal or stay-topmost window behavior.
- Includes Glacier, Aurora, and Blueprint visual variants.
- Keeps colors, fonts, transparency, and cadence editable in `Variables.inc`.

## Requirements

- Windows 10 or Windows 11
- Rainmeter 4.5 or newer
- A locally installed and signed-in Codex application / CLI
- Windows .NET Framework 4.x compiler when building the native listener from source

## Install

1. Download `CodexQuotaOptions_1.5.0.rmskin` from the latest GitHub Release.
2. Open the package with Rainmeter Skin Installer.
3. Load `CodexQuotaOptions\Glacier\Glacier.ini` if it is not loaded automatically.

Glacier starts in the normal window layer (`TOP OFF`), allowing games and other application windows to cover it. Click the bottom-center switch to enable `TOP ON`.

## Runtime behavior

| State | Remote refresh | Animation |
| --- | --- | --- |
| `TASK ACTIVE` | Immediately, then every 20 seconds | Breathing and scan effects |
| `QUIET` | None until resumed or the task ends | Static; task monitoring stays active |
| `SYNCING` | One request in progress | Stronger breathing |
| `SILENT` | None | Static |
| Manual `SYNC` | One request | Sync animation |

The lightweight native listener watches local task lifecycle events and reuses a local `codex app-server` process only while a task is active. It stops the helper process when returning to `SILENT`.

The `QUIET` button is scoped to the current active-task period. `RESUME` performs an immediate refresh and restores the 20-second cadence; when the last active task ends, quiet mode clears automatically and the normal final snapshot runs. If the skin or listener restarts while that task is still active, the applied quiet state is restored without replaying an already-consumed stale token.

## Privacy

- Reads local task event files under `%USERPROFILE%\.codex\sessions` to detect task start, completion, or abort events.
- Requests quota information through the locally installed Codex process.
- Does not contain API keys, account tokens, analytics, or third-party telemetry.
- Does not upload session files or quota data anywhere.

Do not attach personal session files or authentication logs to bug reports.

## Customize

Right-click Glacier and select **Edit dynamic panel settings**, or edit:

```text
Skins\CodexQuotaOptions\@Resources\Variables.inc
```

The dynamic-state UI logic is in `DynamicPanel.lua`; the event listener is in `CodexQuotaAgent.cs` and `SessionEventScanner.cs`.

## Build

From Windows PowerShell:

```powershell
.\Build-Release.ps1 -Version 1.5.0
```

This compiles the native listener, creates the `.rmskin` package in `dist`, and writes `SHA256SUMS.txt`. Generated executables and packages are intentionally excluded from source control.

## Disclaimer

This is an independent community project and is not affiliated with, endorsed by, or maintained by OpenAI. Codex is a trademark of its respective owner. The project relies on local Codex implementation details that may change in future releases.

## License

[MIT](LICENSE)
