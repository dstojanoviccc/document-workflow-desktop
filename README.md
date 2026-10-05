# Document Workflow Desktop

A .NET/WPF desktop application demonstrating document checkout, local editing, version tracking, conflict handling and reliable check-in workflows.

## Overview

This fresh, generic public implementation is built in incremental, tested milestones. **Phase 3 implements SHA-256 local modification detection**, building on checkout, Open and discard. Check-in and conflicts remain roadmap items.

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
- Content-based Unchanged / Modified states, evaluated at startup, Refresh, application activation and before Open/discard.
- Baseline/current hash details, restrained amber Modified styling and explicit protection against discarding local edits.
- Missing or inaccessible working files show a warning and disable Open, without silently recreating files.
- Detailed checkout metadata, restrained WPF styling, MVVM, DI, hosting and structured local logs.
- Automated domain, SQLite, workspace, hashing, workflow, concurrency and view-model tests.

Example: **Available → Check Out → Unchanged → Edit/save externally → Modified → Discard → Available**. Restoring the exact original bytes returns a working copy to Unchanged.

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
- `WorkingCopyStateService`: asynchronous local-file evaluation, SHA-256 comparison and safe missing/unreadable results.
- `WorkflowStore`: SQLite write transactions and atomic document/working-copy metadata changes.
- `LocalWorkspaceService`: deterministic copy paths, path validation, existence checks and staged deletion.
- `FileHashService`: reusable streaming SHA-256 hashing.
- `DemoDocumentSource`: safe source-file resolution.
- `ShellWorkingCopyOpener`: Windows shell file associations.
- `DatabaseInitializer`: migration, first-run seeding and upgrade of Phase 1 placeholder hashes.

Phase 1 databases are retained. Placeholder initial-version hashes are replaced with real demo-file hashes without changing document IDs or creating new versions. Existing content hashes and checkout metadata are preserved. No schema change was needed in Phases 2 or 3.

### Derived working-copy state

SQLite retains the checkout workflow fact, base version, checkout time and baseline SHA-256. Unchanged/Modified is derived from the current local file, rather than stored redundantly: matching hashes mean Unchanged; differing hashes mean Modified. Timestamps and file sizes do not determine state. Startup recalculates this comparison, including edits made while the app was closed.

Filesystem/hash work runs asynchronously off the WPF UI thread through a serialized evaluator. MainViewModel presents results; MainWindow forwards activation requests. Activation requests during another operation are coalesced. There is no timer, polling or FileSystemWatcher. If a file is saved while the app stays active, use Refresh.

A missing file has an explicit evaluation issue and no content state. A temporarily unreadable file retains the last reliable state, marked unverified, with a warning and no current hash. That cache exists only for the current application session; a locked file on a fresh restart shows State unavailable until it can be read. Logs capture evaluations, transitions and failures without file contents.

Details show working state, local path, checkout time, base version, shortened baseline/current hashes and comparison text. Exact restoration means byte-for-byte restoration; opening and resaving identical-looking Office content can change package bytes and still count as Modified.

### Workflow safety

SQLite write transactions serialize workflow mutations, including competing app instances. Working-copy metadata has a unique document key. Copy uses CreateNew and never overwrites an existing working file. If checkout persistence fails, its newly created file is removed where possible.

Discard first moves the expected local file to a sibling `.discard` staging file. A database failure restores it. After metadata commit, the staging file is deleted. If final deletion fails, the user receives cleanup guidance. Unrelated files in a document directory are retained; empty document directories are removed without recursive deletion. Workspace and source roots must be separate, stored paths must match the expected document path, and linked paths are rejected.

Before confirmation, discard refreshes state. Modified or unverified content uses an explicit warning that local edits will be permanently deleted; No preserves the file and checkout. The service evaluates again before staging deletion and refuses a newly modified file if only the unchanged warning was confirmed. This reduces stale-confirmation risk, but does not lock out an external editor for the entire confirmation/deletion interval.

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
2. Check out Product-Catalog.xlsx. Without Refresh, confirm its status becomes Unchanged, Check Out disappears, and Open and Discard Checkout appear.
3. Open View Details. Confirm checkout time, base version, base hash and local path. Verify that the physical file exists at that path.
4. Click Open and confirm the default spreadsheet application opens the local file. Make a small edit and save. Return to the desktop app and confirm Modified; Refresh also reevaluates it.
5. Close and reopen the desktop app. Confirm the checkout, path, Open and Discard Checkout survive, with no second local file or Check Out action.
6. Choose Discard Checkout, then No. Confirm that the working file and checkout remain.
7. Close the external editor, choose Discard Checkout and confirm Yes. Confirm the local file is removed, the row immediately becomes Available, and the central source and v1 remain unchanged.
8. Repeat checkout and Open with a DOCX and the PDF to verify your Windows associations and file compatibility.
9. Check out the PDF again, close the desktop app and external viewer, and delete only that isolated local working file using the path from View Details. Reopen the desktop app. Confirm a warning, disabled Open, retained Discard Checkout and no automatically recreated file. Discard to clear the stale checkout.
10. For a locked-file check, keep a local working file locked by an editor that denies deletion and try discard. Expect a useful failure message and retained checkout. Close the editor and retry. Editors that permit rename/delete may not reproduce the lock case.

### Phase 3 acceptance tests

Use an isolated DataDirectory as shown above. Close external editors before replacing or deleting their files.

- **A — unchanged:** launch, check out Product Catalog, verify Unchanged, restart and verify Unchanged again.
- **B — modified:** Open Product Catalog in Excel, change a cell, save and return to the app. Verify Modified without Refresh. Restart and verify Modified again.
- **C — restore:** close Excel, copy the exact original Product-Catalog.xlsx bytes from the executable's `demo-data` directory onto the local path shown in Details. Click Refresh and verify Unchanged and matching hashes. Do not overwrite the central source. A Debug build's source fixture is `src/DocumentWorkflow.App/bin/Debug/net9.0-windows/demo-data/Product-Catalog.xlsx`.
- **D — discard:** edit/save again, return and verify Modified. Choose Discard Checkout and verify the explicit permanent-loss warning. Choose No; verify the file still exists and remains Modified. Close Excel, discard again and choose Yes; verify local deletion, Available state, and unchanged central source/v1.
- **E — missing:** check out a document, record its local path, close the app/editor, remove only that isolated working file and restart. Verify a clear missing warning, disabled Open, no crash and no recreated file. Discard can clear the stale checkout.

For a read-lock check, use an application that denies reads. Refresh should warn and show the last reliable state as unverified; a fresh restart cannot know that prior state. Close the locking application and Refresh to recover.

### Verification

Automated coverage includes real SQLite migration and persistence, Phase 1 placeholder upgrades, seed idempotency, deterministic file copies, SHA-256 comparisons, checkout and discard, duplicate and competing checkouts, restart through service recreation, missing source/local files, uncreatable workspace, locked files, persistence failures, mismatched workspace paths, simulated absent file association, UI action transitions and confirmation cancellation. Filesystem tests use isolated temporary directories, never the normal LocalAppData workspace.

Phase 3 has **50 passing tests**. Added coverage includes same-size content edits, timestamp-only changes, exact restoration, exclusive read locks and recovery, missing state, activation coalescing, baseline/current details, fresh-service restart reconciliation, and actual view-model discard commands with cancellation, confirmation and an edit during confirmation. A valid XLSX fixture is edited inside its OOXML package without requiring Office; source hashes and version metadata remain unchanged.

Desktop verification used an isolated temporary database and confirmed launch, checkout showing Unchanged, a valid XLSX cell edit while minimized becoming Modified on activation, and differing hash details. Actual process restarts detected Modified and then Unchanged after exact byte restoration. The details panel was subsequently made compact and scrollable and build-verified. The modified warning/cancellation/deletion paths are covered by automated view-model/filesystem tests; Excel interaction and the final compact layout remain available for user acceptance above. Earlier Phase 2 desktop checks covered PDF shell dispatch, confirmation cancellation and missing-file reconciliation. Word fixtures passed package checks but their bundled visual renderer was unavailable in this environment.

## Roadmap

After Phase 3 review:

- Phase 4: actual check-in and new version creation, with an explicit persistence/failure design and tests.
- Later: backend conflict detection and explicit conflict resolution.
- Later: check-in retry and idempotency behavior.
- Later: crash recovery improvements and audit history.

Phase 3 does not include FileSystemWatcher, check-in, new workflow versions or conflicts. Content comparison uses whole-file SHA-256; evaluation cost grows with file size, and a live external write can require another Refresh. Stronger concurrent-edit/crash recovery remains future work.

## Screenshots

The Phase 3 state labels and hash details have been inspected during desktop verification. Saved screenshots are pending manual acceptance.
