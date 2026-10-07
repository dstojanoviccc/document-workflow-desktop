Document Workflow Desktop is a Windows x64 application for safe local editing of versioned documents, with explicit check-in, conflict protection, immutable history and an append-only audit timeline.

Choose the per-user `-setup.exe` installer or extract the portable ZIP. Both include the .NET runtime. XLSX/DOCX/PDF opening requires an appropriate installed editor/viewer.

First launch starts with an empty library. **Load demo data** is an optional evaluation action. Application state remains under `%LOCALAPPDATA%\DocumentWorkflowDesktop`, shared by installed and ZIP builds. Back up this entire directory before upgrading. Uninstall removes binaries and shortcuts and preserves state.

Compare downloaded files against `SHA256SUMS.txt`. Packages are unsigned: Windows may display publisher/reputation warnings. Follow your organization's software policy; no signing credentials or updater are included.

Older audit actions are not reconstructed, inspection copies may accumulate, and filesystem recovery/audit writes are not physically atomic. Conflict prevents stale check-in and preserves local edits; there is no forced overwrite or automatic merge.
