# Recovery and backup

Check-in spans SQLite and a filesystem, which cannot share one physical transaction. Staging records and verified artifact hashes bridge that boundary. Startup/Refresh reconciliation preserves referenced committed versions, recovers tracked interrupted operations and quarantines unreferenced artifacts in managed version storage.

Unknown files stay untouched. Corrupted storage is outside normal workflow recovery. Recovery filesystem actions and their audit database writes are not one indivisible transaction; conservative preservation takes precedence over deletion or invented audit events.

Conflict recovery preserves local edits until an explicit confirmed discard. Inspection copies are isolated from both working files and immutable versions. Export creates a separate copy and does not resolve a stale checkout by overwriting the latest content.

## Backup

Close the application and external editors first. Back up the **complete** `%LOCALAPPDATA%\DocumentWorkflowDesktop` directory, including SQLite, workspace, versions, recovery and inspection content. If configuration overrides workspace/data paths, back up those locations together. Do not back up only `documents.db`: its references rely on corresponding file bytes. Retain the same application/demo source package when restoring legacy v1 content references.

Installer upgrades and uninstall preserve user state. Uninstall is not a backup or a data-reset action. Manual deletion of the data directory is destructive and should happen only after a deliberate backup decision.
