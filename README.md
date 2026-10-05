# Document Workflow Desktop

A .NET/WPF desktop application demonstrating document checkout, local editing, version tracking, conflict handling and reliable check-in workflows.

## Overview

This fresh, generic public implementation is built in incremental, tested milestones. **Phase 2 implements local workspace checkout, Open and discard.** Automatic modification detection, check-in and conflicts remain roadmap items.

Five valid, generic XLSX, DOCX and PDF fixtures are shipped in `demo-data/`. Their copies in the application output directory simulate a central document repository. Checkout creates a separate physical working file; external editors never open the central file through the application.

## Features

- SQLite-backed document listing, version metadata and persistent working-copy metadata.
- Real demo source files included in build and publish output.
- Deterministic local workspace paths with a directory per document ID.
- Checkout with duplicate protection, preserved base version, SHA-256 baseline and checkout timestamp.
- Immediate state-aware row actions: Check Out and View Details when available; Open, Discard Checkout and View Details when checked out.
- Open through the default Windows file association, without hardcoded Office paths.
- Confirmed discard that removes the local copy and checkout metadata while retaining source files and version history.
- Checkout persistence across restart.
- Startup and refresh reconciliation of local-file presence; missing or inaccessible files show a warning and disable Open.
- Detailed checkout metadata, restrained WPF styling, MVVM, DI, hosting and structured local logs.
- Automated domain, SQLite, workspace, hashing, workflow, concurrency and view-model tests.

A checked-out document remains **Checked out** even after external edits. Phase 2 records a baseline hash but does not compare it automatically or mark a document Modified.

## Architecture

```text
DocumentWorkflow.sln
demo-data/                         Generic, openable source fixtures
src/
  DocumentWorkflow.Domain          Entities, defaults, validation and checkout state transitions
  DocumentWorkflow.Application     Contracts and document workflow use cases
  DocumentWorkflow.Infrastructure  EF Core SQLite, migrations, workspace, hashing and shell opening
  DocumentWorkflow.App             WPF views, view models, commands and DI composition root
tests/
  DocumentWorkflow.Tests           xUnit domain, persistence, filesystem, workflow and MVVM tests
```

Dependencies flow from App to Infrastructure to Application to Domain. Domain has no UI or persistence dependencies. Application orchestrates workflow through interfaces; MainViewModel coordinates commands and presentation only. UserDialogService owns the discard confirmation.

Key components:

- `DocumentWorkflowService`: checkout, Open, discard, library snapshots and startup reconciliation.
- `WorkflowStore`: SQLite write transactions and atomic document/working-copy metadata changes.
- `LocalWorkspaceService`: deterministic copy paths, path validation, existence checks and staged deletion.
- `FileHashService`: reusable streaming SHA-256 hashing.
- `DemoDocumentSource`: safe source-file resolution.
- `ShellWorkingCopyOpener`: Windows shell file associations.
- `DatabaseInitializer`: migration, first-run seeding and upgrade of Phase 1 placeholder hashes.

Phase 1 databases are retained. Placeholder initial-version hashes are replaced with real demo-file hashes without changing document IDs or creating new versions. Existing content hashes and checkout metadata are preserved. No schema change was needed in Phase 2.

### Workflow safety

SQLite write transactions serialize workflow mutations, including competing app instances. Working-copy metadata has a unique document key. Copy uses CreateNew and never overwrites an existing working file. If checkout persistence fails, its newly created file is removed where possible.

Discard first moves the expected local file to a sibling `.discard` staging file. A database failure restores it. After metadata commit, the staging file is deleted. If final deletion fails, the user receives cleanup guidance. Unrelated files in a document directory are retained; empty document directories are removed without recursive deletion. Workspace and source roots must be separate, stored paths must match the expected document path, and linked paths are rejected.

These operations provide ordinary failure handling, not full crash recovery. If the process or machine stops between filesystem and database steps, an untracked local file or `.discard` file can remain. Startup warns about missing tracked files and never recreates or overwrites them. Advanced recovery is deferred.

## Tech Stack

- .NET 9 and C#
- WPF and MVVM
- SQLite and EF Core 9
- Microsoft.Extensions.Hosting, configuration, dependency injection and logging
- xUnit

## Getting Started

Requirements: Windows and the .NET 9 SDK. Visual Studio is optional; if used, install the .NET desktop development workload. A compatible default application is needed to open each file type; Office itself is not required to build or run the desktop application.

From the repository root, with the application closed:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DocumentWorkflow.App
```

Or open `DocumentWorkflow.sln` and select `DocumentWorkflow.App` as the startup project. Close the app before rebuilding, because the running process locks its output assemblies.

### Data and workspace paths

Defaults:

```text
%LOCALAPPDATA%\DocumentWorkflowDesktop\documents.db
%LOCALAPPDATA%\DocumentWorkflowDesktop\workspace\{document-id}\{file-name}
%LOCALAPPDATA%\DocumentWorkflowDesktop\logs\workflow-yyyy-MM-dd.jsonl
```

Source files are loaded from `demo-data` beside the application executable. The repository's `demo-data` directory is source-controlled; runtime working copies are not stored in the repository.

Configuration is available through command-line arguments or environment variables, without a required appsettings file:

```powershell
dotnet run --project src/DocumentWorkflow.App -- --DocumentWorkflow:DataDirectory C:\Temp\DocumentWorkflowDemo
```

Optional keys:

| Key | Default |
| --- | --- |
| `DocumentWorkflow:DataDirectory` | Current user's LocalAppData application directory |
| `DocumentWorkflow:WorkspaceDirectory` | `workspace` under DataDirectory |
| `DocumentWorkflow:DemoSourceDirectory` | `demo-data` beside the executable |

Use absolute directory paths. Environment variables use double underscores, for example `DocumentWorkflow__WorkspaceDirectory`. Keep workspace configuration stable while checkouts exist; changing it produces a warning and prevents operations against mismatched stored paths.

Use a fresh DataDirectory for an isolated demo reset. There is no reset button and no automatic deletion of existing data. Restore missing central fixtures by restoring the repository's `demo-data` files and rebuilding; initialization does not silently recreate a user's working copy.

Logs record workflow starts, completions, failures and startup reconciliation with document IDs and named properties. No file contents are logged. Log files currently have no automatic retention policy.

### Migrations

Migrations apply automatically at startup. Development commands:

```powershell
dotnet tool restore
dotnet ef migrations list --project src/DocumentWorkflow.Infrastructure
dotnet ef migrations add MigrationName --project src/DocumentWorkflow.Infrastructure
```

The design-time context uses `document-workflow.db` in the command's current directory, separate from the application's runtime database. Do not expect design-time database updates to affect runtime data.

### Manual acceptance check

1. Launch the app and verify five Available documents, each at v1.
2. Check out Product-Catalog.xlsx. Without Refresh, confirm its status becomes Checked out, Check Out disappears, and Open and Discard Checkout appear.
3. Open View Details. Confirm checkout time, base version, base hash and local path. Verify that the physical file exists at that path.
4. Click Open and confirm the default spreadsheet application opens the local file. Make a small edit and save. Status should remain Checked out in Phase 2.
5. Close and reopen the desktop app. Confirm the checkout, path, Open and Discard Checkout survive, with no second local file or Check Out action.
6. Choose Discard Checkout, then No. Confirm that the working file and checkout remain.
7. Close the external editor, choose Discard Checkout and confirm Yes. Confirm the local file is removed, the row immediately becomes Available, and the central source and v1 remain unchanged.
8. Repeat checkout and Open with a DOCX and the PDF to verify your Windows associations and file compatibility.
9. Check out the PDF again, close the desktop app and external viewer, and delete only that isolated local working file using the path from View Details. Reopen the desktop app. Confirm a warning, disabled Open, retained Discard Checkout and no automatically recreated file. Discard to clear the stale checkout.
10. For a locked-file check, keep a local working file locked by an editor that denies deletion and try discard. Expect a useful failure message and retained checkout. Close the editor and retry. Editors that permit rename/delete may not reproduce the lock case.

### Verification

Automated coverage includes real SQLite migration and persistence, Phase 1 placeholder upgrades, seed idempotency, deterministic file copies, SHA-256 comparisons, checkout and discard, duplicate and competing checkouts, restart through service recreation, missing source/local files, uncreatable workspace, locked files, persistence failures, mismatched workspace paths, simulated absent file association, UI action transitions and confirmation cancellation. Filesystem tests use isolated temporary directories, never the normal LocalAppData workspace.

Desktop verification used an isolated temporary database and confirmed launch, checkout, immediate action updates, Windows PDF shell dispatch, the discard warning and cancellation, persisted checkout after process restart, and missing-file reconciliation after another restart. Successful discard and failure rollback are covered by filesystem/SQLite tests. Opening the Word and Excel fixtures in your installed applications remains part of manual acceptance; Word fixtures passed package checks but their bundled visual renderer was unavailable in this environment.

## Roadmap

After Phase 2 manual acceptance:

- Phase 3: compare local content to the stored baseline hash and present unchanged/modified state; design file-change notification separately before implementation.
- Later: check-in and version creation, with retry and idempotency behavior.
- Later: conflict detection and explicit resolution.
- Later: crash recovery improvements and audit history.

Phase 2 does not include automatic modification detection, FileSystemWatcher, check-in, new workflow versions or conflicts.

## Screenshots

The Phase 2 library has been visually inspected during desktop verification. Saved screenshots are pending manual acceptance.
