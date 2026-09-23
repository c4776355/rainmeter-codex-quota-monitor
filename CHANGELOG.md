# Changelog

## Unreleased

No changes yet.

## 1.6.0 - 2026-09-23

### Added

- Added a persistent `5 HOURS / 1 WEEK` quota-pool selector to Glacier's existing pool card.
- Cached both returned quota windows for an immediate display change, followed by a fresh quota request on each pool-button click. A refresh while quiet returns to quiet mode after completion.
- Added regression coverage for a freshly reset 5-hour pool, reversed `primary` / `secondary` order, and single-window fallback.

### Fixed

- The display no longer switches to the weekly percentage after the 5-hour pool resets. Pools are now identified by `windowDurationMins` instead of whichever window has less remaining quota.
- The percentage, progress bar, reset countdown, restore date, and cycle caption now always come from the same selected pool.
- Raised the quota-pool heading and centered both pool labels horizontally and vertically in the selector pill.
- Removed the divider below the pool selector and moved the pill and its centered label down by three logical pixels.

### Verification

- Live Rainmeter meter bounds confirmed that both `5 HOURS` and `1 WEEK` share the pill's center, with the quota-pool heading raised by three logical pixels.
- Pool clicks refreshed the quota in both active and quiet modes. Quiet mode resumed with no scheduled query during a further 22-second observation; active mode scheduled its next query 20 seconds after the click refresh.
- Quota-pool regression tests and the native-agent build passed.

## 1.5.0 - 2026-08-25

### Added

- Added a matched `QUIET / RESUME` button beside the bottom-row `TOP` control.
- Quiet mode pauses scheduled queries, stops breathing and scan effects, and preserves task monitoring.
- Resuming performs an immediate refresh; quiet mode clears automatically when the task ends.

### Fixed

- Quiet mode now survives a skin or background-agent restart while the same task remains active.
- The quiet control no longer launches a redundant background-agent process.

### Regression investigation and verification

- Restart recovery failed because agent initialization reset `ManualSilent` and treated the current quiet token as already consumed. Startup now reconciles the current token, the last processed token, and the previously applied state; a processed stale `QUIET=1` token is not carried into a later inactive period.
- Both quiet-button hit areas also invoked the agent launcher. The named mutex prevented duplicate quota requests, but the extra process could wait for up to eight seconds and briefly race state initialization. The quiet control now sends only its token command.
- Live Rainmeter regression checks confirmed that quiet mode issued no scheduled query for 22 seconds, owned no `codex app-server` child, survived both graceful and forced agent restarts, retained quiet mode after a one-time manual sync, and resumed with an immediate sync followed by the configured 20-second cadence.
- Release checks parsed all eight PowerShell scripts, compiled the native agent, and built a valid 18-entry RMSKIN package with no runtime state or legacy binaries.

## 1.4.1

- Added task-aware active polling and a fully static silent state.
- Added a 20-second active countdown and final refresh on task completion.
- Added full-edge breathing and scan effects for active and syncing states.
- Added highlighted reset countdown with two-line restore date and time.
- Added a persistent `TOP OFF / TOP ON` Z-position switch.
- Refined text alignment, footer spacing, transparency, and typography.
- Added reproducible Windows build and release scripts.
