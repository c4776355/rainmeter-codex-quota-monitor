# Security and privacy

This project reads local Codex task events and starts the locally installed `codex app-server` process to request quota information. It does not require users to paste credentials into the skin.

When reporting a security issue, do not attach `.codex` session files, authentication logs, access tokens, or personal quota data. Describe the behavior and affected version with sanitized reproduction steps.

Generated release binaries are compiled from `CodexQuotaAgent.cs` and `SessionEventScanner.cs` by the repository's Windows build workflow. Release packages include SHA-256 checksums.
