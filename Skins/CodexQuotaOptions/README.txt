CODEX QUOTA OPTIONS 1.4.1

Glacier is a 380 x 248 px task-aware quota panel:
  - immediate sync on task start
  - one query every 20 seconds while active
  - final sync and fully static SILENT mode after completion
  - highlighted reset countdown with large two-line restore date/time
  - centered lower detail cells and balanced main quota spacing
  - remaining percentage finalized at Y=38
  - bottom-center TOP OFF / TOP ON persistent Z-position toggle
  - aligned top-right status and raised footer labels
  - full-edge and whole-panel breathing only while active or syncing

Right-click Glacier and choose "Edit dynamic panel settings" to open
@Resources\Variables.inc. The native listener source is editable at
@Resources\Scripts\CodexQuotaAgent.cs and recompiles after source changes.

Aurora and Blueprint remain as the compact 304 x 184 px alternatives.
