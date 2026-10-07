# Installation and release engineering

## Packages

Windows x64 packages are self-contained: no separate .NET runtime installation is needed. Supported deployment requires a Windows version supported by .NET 9 (prefer current Windows 11). The installer declares Windows 10 as its minimum; this is not a promise of support for every obsolete Windows build.

Release assets are `DocumentWorkflowDesktop-1.0.0-win-x64-setup.exe`, `DocumentWorkflowDesktop-1.0.0-win-x64.zip` and `SHA256SUMS.txt`. They appear on GitHub Releases after the owner publishes the matching tag. The local builds are review artifacts; no release has been uploaded yet.

Run the installer for your current user, or extract **all** ZIP contents into a stable directory and start `DocumentWorkflow.App.exe`. Office/PDF editing requires an appropriate external editor and file association. First startup applies SQLite migrations and creates directories automatically, with an empty catalogue. Load the optional samples explicitly.

## Why Inno Setup plus ZIP

Inno Setup gives this small WPF application a familiar per-user installer, Start menu shortcut, stable upgrade identity and uninstall entry without enterprise deployment or signing credentials. A ZIP provides a transparent alternative for evaluation. MSIX identity/certificate distribution and WiX/MSI complexity do not benefit the current local scope enough to justify introducing them.

The installer requests no elevation, uses a stable AppId and defaults to `%LOCALAPPDATA%\Programs\DocumentWorkflowDesktop`. It installs only application files and packaged read-only samples. Upgrade replaces those files. Uninstall removes tracked binaries and the shortcut, leaving user state intact. Close the app and external editors before upgrade/uninstall.

## User state

The existing root remains `%LOCALAPPDATA%\DocumentWorkflowDesktop`:

| Content | Location |
| --- | --- |
| SQLite metadata, checkout and audit | `documents.db` |
| Working copies and isolated inspection copies | `workspace`, with inspection copies under `.inspection` |
| Immutable versions and managed staging artifacts | `versions` |
| Quarantined recovery content | `recovery` |
| Structured local logs | `logs` |

Reinstalling uses this surviving state intentionally. It does not silently reset the library or seed samples. Writable state is independent of executable location. Advanced configuration uses `DocumentWorkflow:DataDirectory`, `DocumentWorkflow:WorkspaceDirectory` and `DocumentWorkflow:DemoSourceDirectory` through .NET configuration. Do not put writable overrides inside the installation directory.

Back up the full state while the application/editors are closed; see [backup and recovery](recovery.md). Binary replacement is not a database downgrade guarantee. Future releases with schema changes must document backward compatibility separately.

## Verify downloads

```powershell
Get-FileHash .\DocumentWorkflowDesktop-1.0.0-win-x64-setup.exe -Algorithm SHA256
Get-FileHash .\DocumentWorkflowDesktop-1.0.0-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Compare each hash with the matching asset entry. Hashes check integrity; they do not replace publisher signing. App/installer artifacts are unsigned, so publisher or SmartScreen warnings may appear. No signing credentials are required or stored.

## Build packages locally

Run from the repository on Windows with the .NET 9 SDK and **PowerShell 7** (the shell used for local validation and GitHub Actions):

```powershell
.\scripts\Build-Release.ps1
```

The script restores the solution, builds Release with warnings as errors, runs all tests, publishes the WPF app, creates a ZIP, compiles the installer and writes SHA-256 sums. It bootstraps Inno Setup 6.7.3 into ignored `artifacts/tools`, verifying its pinned SHA-256 and Authenticode publisher before execution. To use an existing compiler:

```powershell
.\scripts\Build-Release.ps1 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The equivalent publish command is:

```powershell
dotnet publish src/DocumentWorkflow.App/DocumentWorkflow.App.csproj -p:PublishProfile=Windows -p:DebugType=none -p:DebugSymbols=false --output artifacts/publish/win-x64 -warnaserror
```

The profile selects Release, `win-x64`, self-contained runtime 9.0.20 and directory output without single-file bundling, trimming or ReadyToRun. The packaging script verifies the version and bundled WPF/runtime files and rejects unexpected state/debug files. Generated outputs live only under ignored `artifacts`; packaging cleans its own publish/versioned release output directories, never user data.

`Directory.Build.props` is the authoritative `1.0.0` application version. Assembly/file/product metadata, footer, artifact names and installer consume it. Deterministic compilation and explicit versions make the procedure repeatable; archive/installer timestamps and SDK servicing mean byte-identical packages across different hosts are not claimed.

## CI and publishing

`ci.yml` runs on pull requests and `main` pushes, on Windows, for Debug and Release: restore → warning-free build → full tests. Major versions of the official checkout/setup-dotnet actions are pinned.

`release.yml` runs only on pushed `v*` tags. It requires an exact stable `vX.Y.Z` match to the authoritative version, runs the proven local script and uploads exactly installer, ZIP and checksums to a GitHub Release using the job-scoped repository token. Normal commits do not publish releases.

After owner review, license decision and successful remote CI, publish the matching annotated tag through the normal repository workflow. This phase has not pushed commits or created a tag/release.
