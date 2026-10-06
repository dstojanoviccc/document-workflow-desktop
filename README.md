# Document Workflow Desktop

A .NET/WPF desktop application demonstrating document checkout, local editing, version tracking, conflict handling and reliable check-in workflows.

## Overview

This fresh, generic public implementation is built in incremental, tested milestones. **Phase 6 adds immutable version history, verified historical inspection and a document workflow timeline**, building on optimistic concurrency, explicit conflict recovery and SHA-256 modification detection.

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
- Explicit Check in for verified Modified copies, new version artifacts, atomic current-version advancement and checkout completion.
- Derived Conflict state for modified stale checkouts, with keep, save-copy, inspect-latest and confirmed-discard choices.
- Startup/Refresh recovery of tracked staging and quarantine of unreferenced managed version artifacts.
- Per-document immutable version history, current/base indicators, compact hashes and copyable metadata.
- Verified isolated historical inspection and append-only typed workflow events with friendly timeline labels.

Example: **Available → Check Out → Unchanged → Edit/save externally → Modified → Discard → Available**. Restoring the exact original bytes returns a working copy to Unchanged.

Check-in flow: **Available v1 → Check Out → Unchanged → Edit/save → Modified → Check in → Available v2**. The next checkout copies v2; v1 remains intact.

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

- `DocumentWorkflowService`: checkout, Open, discard, check-in, library snapshots and startup reconciliation; its partial `ConflictWorkflow.cs` contains competing publication and non-destructive recovery actions.
- `WorkingCopyStateService`: asynchronous local-file evaluation, SHA-256 comparison and safe missing/unreadable results.
- `WorkflowStore`: SQLite write transactions and atomic document/working-copy metadata changes.
- `LocalWorkspaceService`: deterministic copy paths, path validation, existence checks and staged deletion.
- `FileHashService`: reusable streaming SHA-256 hashing.
- `LocalVersionContentStore` / `IVersionContentStore`: separate immutable version paths, durable copy and verification of stored bytes.
- `LocalWorkflowRecovery` / `IWorkflowRecovery`: restore active staging, preserve referenced versions and quarantine unreferenced managed artifacts under a SQLite writer lock.
- `DemoDocumentSource`: safe source-file resolution.
- `ShellWorkingCopyOpener`: Windows shell file associations.
- `DatabaseInitializer`: migration, first-run seeding and upgrade of Phase 1 placeholder hashes.

Phase 1 databases are retained. Placeholder initial-version hashes are replaced with real demo-file hashes without changing document IDs or creating new versions. Existing content hashes and checkout metadata are preserved. No schema change was needed in Phases 2 or 3.

Phase 4 adds the nullable `DocumentVersion.BaseVersion` column through an additive EF migration. Existing version rows are retained. New versions record their origin, SHA-256, creation time and deterministic next number; the document's updated time equals the new version's creation time. The existing unique document/version index remains in force. Phase 6 exposes the persisted versions through the history window reached from Details.

### Check-in storage and transaction

v1 uses the packaged source artifact; v2 and later use `DataDirectory/versions/{document-id}/{version-number}/{file-name}`. Check-in never writes to v1 or overwrites a version artifact. Version immutability is enforced by application operations and CreateNew, rather than OS ACLs: manually editing application storage is unsupported.

Check-in runs off the UI thread and begins a serialized SQLite write transaction. It reevaluates through the canonical evaluator, rejects absent/unchanged/missing/unreadable checkouts and checks the persisted checkout base still matches the current version. A stale base returns structured data rather than silently creating another version; explicit recovery choices are described below.

The service copies under a read handle that denies external writing/deletion, flushes the artifact to disk and hashes the stored artifact. It must match the fresh evaluation hash; otherwise the unpublished artifact is removed and the checkout remains. After preparation, the local copy is renamed to the existing reversible `.discard` staging path. Its hash is checked again under a read lock through database commit, detecting edits between copying and staging.

A single SQLite transaction inserts the new version, advances the document and removes active checkout metadata. Before commit, failures roll back database changes, restore the staged working file and remove the artifact created by that attempt. Existing destination artifacts are never removed or overwritten. Only after successful commit is the staged local file deleted. Cleanup failure reports a success-with-cleanup warning; it does not undo the durable version. UI rows refresh immediately to Available at the new version.

Filesystem and SQLite are not one physical transaction. A process/machine crash can leave an unpublished version artifact or staged local file. Phase 5 reconciles these at startup and Refresh. Before rollback cleanup after a completion error, check-in and competing publication consult committed version metadata; a durable version is preserved even if completion reporting failed. If metadata cannot be verified, cleanup is deferred rather than deleting an unverified artifact. Back up the database, versions directory and working copies together; keep packaged v1 fixtures available. Close external editors before check-in, since incompatible open handles fail safely.

### Conflict invariant and results

Conflict is derived when an active copy has a successful hash comparison showing local edits **and** `WorkingCopy.BaseVersion != DocumentRecord.CurrentVersion`. Both version numbers come from SQLite; timestamps and filenames do not decide concurrency. No Conflict state is persisted and no new schema migration is needed in Phase 5.

`CheckInWithResultAsync` is the structured application/UI entry point. `CheckInResult` reports success, a message and optional `CheckoutConflict(BaseVersion, CurrentVersion, WorkingPath, HasLocalEdits)`. The Phase 4 `CheckInAsync` entry point remains a compatibility wrapper over the same implementation. A stale-base result keeps metadata and local bytes and creates no version. The UI opens recovery Details on a stale-row check-in attempt.

An unchanged stale copy stays Unchanged: it has no verified local edits, but check-in remains blocked and Details explain the older base. Missing and unreadable copies keep their distinct evaluation issues; they never invent local edits. Last reliable state on read failure remains explicitly unverified. No recovery silently changes the checkout baseline.

Trusted application/test code can simulate another logical writer with `DocumentWorkflowService.PublishCompetingVersionAsync(documentId, writerContentPath)`. This creates and hashes a separate immutable artifact, advances current metadata transactionally through a domain method and leaves any checkout untouched. It rejects using that active checkout as the writer's source. It has no general ingestion UI, networking or fake server.

Transitions:

- Modified v1 checkout + competing v2 publication → Conflict, base v1/current v2; check-in creates no v3.
- Conflict + keep, dismiss, inspect or export → Conflict with local edits intact.
- Conflict + cancelled discard → Conflict with local edits intact.
- Conflict + confirmed discard → Available at current v2; the next checkout uses v2.

### Explicit recovery choices

Choose **Details / Recover** on a stale row. Keep my local edits performs no filesystem or metadata mutation. Open my copy continues to use the checkout. Open latest creates a separate read-only inspection file under `workspace/.inspection/{document-id}/v{number}/{unique-id}/{file-name}` and dispatches that copy through the Windows association, without creating another checkout or exposing the immutable source for editing.

Save local copy uses a Save File dialog and CreateNew copying under restrictive sharing. Cancellation preserves everything; an existing destination is refused even if selected. Destinations inside the workspace, packaged source or version storage are rejected. Export retains exact captured bytes and leaves the checkout active. Inspection copies are retained for now; read-only is an inspection aid, not an OS security guarantee.

Discard local checkout reuses the permanent-loss warning and Phase 3 confirmation guard, including Conflict as edited content. No is the default. No force overwrite, automatic rebase, merge or check-in-anyway option exists.

### Interrupted-operation reconciliation

The database is authoritative for valid version references. Recovery holds the same per-database SQLite writer transaction as check-in, so it cannot quarantine an artifact while a normal publication is preparing it. It examines only known document IDs and exact deterministic managed paths:

| Observed artifact | Recovery action |
| --- | --- |
| Active checkout; expected file absent; exact `.discard` exists | Restore staged content to the expected local path without overwrite |
| Active checkout already has a file; an extra `.discard` exists | Keep active bytes; quarantine the staged copy |
| No active checkout; exact `.discard` exists | Quarantine staged content; do not recreate an active checkout |
| Numeric v2+ directory with exact managed filename but no SQLite version reference | Quarantine the artifact, including partially written content |
| Referenced version artifact | Leave it intact, regardless of current-version pointer |
| Referenced artifact missing | Warn; retain metadata and request restoration from backup |
| Unknown filename, directory, document ID or untracked local file | Leave it untouched |

Quarantine moves bytes into `DataDirectory/recovery` with the document ID and a unique name; it does not delete content. Recovery notices include the preserved path and are logged. Locked artifacts or path-validation failures produce guidance and defer recovery until access returns. A repeated Refresh is idempotent. The rule also handles Phase 4 leftovers; no old operation journal is required.

Limits: separate databases must not share storage roots; recovery serialization relies on the shared database. Unknown paths, orphan checkouts without tracking and damaged metadata require manual investigation. Power loss/storage corruption and durable retry identities remain outside this phase. Recovery does not reconstruct missing content, auto-import quarantined files or automatically clean inspection/quarantine directories.

### Derived working-copy state

SQLite retains the checkout workflow fact, base version, checkout time and baseline SHA-256. Unchanged/Modified is derived from the current local file, rather than stored redundantly: matching hashes mean Unchanged; differing hashes mean Modified. Timestamps and file sizes do not determine state. Startup recalculates this comparison, including edits made while the app was closed.

Filesystem/hash work runs asynchronously off the WPF UI thread through a serialized evaluator. MainViewModel presents results; MainWindow forwards activation requests. Activation requests during another operation are coalesced. There is no timer, polling or FileSystemWatcher. If a file is saved while the app stays active, use Refresh.

A missing file has an explicit evaluation issue and no content state. A temporarily unreadable file retains the last reliable state, marked unverified, with a warning and no current hash. That cache exists only for the current application session; a locked file on a fresh restart shows State unavailable until it can be read. Logs capture evaluations, transitions and failures without file contents.

Details show working state, local path, checkout time, base version, shortened baseline/current hashes and comparison text. Exact restoration means byte-for-byte restoration; opening and resaving identical-looking Office content can change package bytes and still count as Modified.

### Workflow safety

SQLite write transactions serialize workflow mutations, including competing app instances. Working-copy metadata has a unique document key. Copy uses CreateNew and never overwrites an existing working file. If checkout persistence fails, its newly created file is removed where possible.

Discard first moves the expected local file to a sibling `.discard` staging file. A database failure restores it. After metadata commit, the staging file is deleted. If final deletion fails, the user receives cleanup guidance. Unrelated files in a document directory are retained; empty document directories are removed without recursive deletion. Workspace and source roots must be separate, stored paths must match the expected document path, and linked paths are rejected.

Before confirmation, discard refreshes state. Modified or unverified content uses an explicit warning that local edits will be permanently deleted; No preserves the file and checkout. The service evaluates again before staging deletion and refuses a newly modified file if only the unchanged warning was confirmed. This reduces stale-confirmation risk, but does not lock out an external editor for the entire confirmation/deletion interval.

These operations provide ordinary failure handling and conservative recovery of known artifacts. A missing tracked file is not recreated from central content; only its existing tracked `.discard` bytes can be restored. Unknown files and ambiguous data are preserved for manual investigation.

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
%LOCALAPPDATA%\DocumentWorkflowDesktop\versions\{document-id}\{version-number}\{file-name}
%LOCALAPPDATA%\DocumentWorkflowDesktop\recovery\{document-id}-{unique-id}-{file-name}
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

Phase 4 has **68 passing tests (18 added)** and adds artifact immutability/hash tests, successful valid-XLSX check-in, exact base-byte preservation, repository/service restart, next checkout from v2, unchanged/missing/exclusive-lock rejection, destination failure, injected metadata failure, SQL-write rollback before commit, competing services, disappearing/changing files during preparation, duplicate UI invocation, immediate row/details updates and verified-state command eligibility. Tests use real SQLite and isolated temporary paths. Check-in success, rollback and races are automated; the desktop smoke test confirmed launch, Unchanged checkout and a Modified XLSX row with the new Check in action and fitting layout. It did not execute the final Check in through desktop automation.

### Phase 4 manual acceptance

1. Launch with a fresh isolated DataDirectory. Check out Product Catalog; verify Unchanged and no Check in action.
2. Open in Excel, edit/save, close Excel and return to the app (or Refresh). Verify Modified and Check in.
3. Click Check in. Verify Available, v2, no active Open/Discard/Check in actions and a success message immediately. Details must show v2.
4. Inspect `versions/{document-id}/2/Product-Catalog.xlsx` under the isolated DataDirectory. Verify its SHA-256 matches the v2 SQLite metadata and the saved edited bytes. The packaged demo v1 must remain byte-identical.
5. Restart; verify Available v2. Check out again; verify base v2, Unchanged and the edited content when opened.
6. Try unchanged, missing and locked-file scenarios. No new version should appear, and failures must retain the checkout and any surviving edits. Restore access and retry.

Desktop verification used an isolated temporary database and confirmed launch, checkout showing Unchanged, a valid XLSX cell edit while minimized becoming Modified on activation, and differing hash details. Actual process restarts detected Modified and then Unchanged after exact byte restoration. The details panel was subsequently made compact and scrollable and build-verified. The modified warning/cancellation/deletion paths are covered by automated view-model/filesystem tests; Excel interaction and the final compact layout remain available for user acceptance above. Earlier Phase 2 desktop checks covered PDF shell dispatch, confirmation cancellation and missing-file reconciliation. Word fixtures passed package checks but their bundled visual renderer was unavailable in this environment.

## Roadmap

After Phase 6 review:

- Phase 7 recommendation: Windows packaging and repeatable release verification, after explicit approval.
- Later: check-in retry and idempotency behavior.
- Later: stronger crash recovery and durable retry identities.

The current application does not include remote APIs, cloud storage, automatic check-in, FileSystemWatcher, merge/force-overwrite workflows, arbitrary external ingestion or installer/release work. Content comparison uses whole-file SHA-256; evaluation cost grows with file size. Stronger recovery and durable retry identities remain future work. Phase 6 is complete; Phase 7 has not started.

## Screenshots

Phase 5 Conflict labels, hash details, recovery controls and permanent-loss confirmation have been inspected during desktop verification. Saved screenshots/marketing polish are deferred.

## Phase 4 implementation files

Exact files added or changed from the approved Phase 3 baseline:

```text
README.md
src/DocumentWorkflow.App/App.xaml.cs
src/DocumentWorkflow.App/Commands.cs
src/DocumentWorkflow.App/MainViewModel.cs
src/DocumentWorkflow.App/MainWindow.xaml
src/DocumentWorkflow.Application/DocumentWorkflowService.cs
src/DocumentWorkflow.Application/IDocumentWorkflowService.cs
src/DocumentWorkflow.Application/IVersionContentStore.cs
src/DocumentWorkflow.Application/IWorkflowStore.cs
src/DocumentWorkflow.Application/IWorkspaceService.cs
src/DocumentWorkflow.Domain/Documents.cs
src/DocumentWorkflow.Infrastructure/LocalVersionContentStore.cs
src/DocumentWorkflow.Infrastructure/LocalWorkspaceService.cs
src/DocumentWorkflow.Infrastructure/Migrations/20261006100726_ImmutableVersions.Designer.cs
src/DocumentWorkflow.Infrastructure/Migrations/20261006100726_ImmutableVersions.cs
src/DocumentWorkflow.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
src/DocumentWorkflow.Infrastructure/WorkflowStore.cs
tests/DocumentWorkflow.Tests/ActivationTests.cs
tests/DocumentWorkflow.Tests/CheckInTests.cs
tests/DocumentWorkflow.Tests/VersionContentTests.cs
tests/DocumentWorkflow.Tests/ViewModelTests.cs
tests/DocumentWorkflow.Tests/WorkflowTests.cs
tests/DocumentWorkflow.Tests/WorkflowViewModelTests.cs
```

### Phase 5 verification and manual acceptance

The Phase 5 baseline had **85 passing tests: all 68 earlier cases plus 17 Phase 5 cases**. Conflict tests cover valid XLSX competing publication, structured stale-base outcomes, restart, simultaneous stale attempts, inspection/export exact bytes, overwrite refusal, cancelled/confirmed discard, unchanged stale copies, missing/unreadable stale copies, active-source rejection and stale UI commands. Recovery tests cover durable uncommitted artifacts, interrupted staging, committed metadata before UI refresh, unknown-file preservation, idempotence, locked staging, recovery/check-in serialization, missing committed storage and completion-reporting errors after check-in and competing publication.

WPF smoke verification used a temporary driver outside the repository to seed five documents, check out/edit a valid XLSX and publish v2 through the real competing-writer service. The desktop showed current v2, base v1, Conflict, no Check in action, differing hashes and all recovery controls. The stronger discard warning was inspected and No was selected; Conflict remained and a package read confirmed the local edit text survived. Open-latest/export/confirmed-discard byte safety is automated; these final actions were not executed through desktop automation.

To exercise the service-level demo path in a local harness/test with configured services:

```csharp
await workflow.CheckOutAsync(documentId);
// Edit/save the managed working file, then prepare a separate valid writer file.
await workflow.PublishCompetingVersionAsync(documentId, writerContentPath);
var result = await workflow.CheckInWithResultAsync(documentId);
// result.Succeeded == false; result.Conflict describes base/current/path/local edits.
```

The existing application service is the competing-writer path; no SQL edits or fake server are needed. Automated scenarios can be run with:

```powershell
dotnet test --filter "FullyQualifiedName~ConflictTests|FullyQualifiedName~RecoveryTests"
```

Manual acceptance with an isolated DataDirectory and a service-prepared conflict:

1. Check out v1, edit/save the local XLSX, then publish competing v2 through the application service above.
2. Return to the desktop or Refresh; verify Conflict, current v2, base v1 and no Check in action.
3. Open Details / Recover; verify that local edits still exist. Keep my local edits must leave Conflict unchanged.
4. Open latest; inspect its v2 content and read-only copy path. Open my copy must still show the original local edits.
5. Save local copy to a new filename; compare its bytes with the checkout. Cancel a second save, then select an existing filename; verify no overwrite.
6. Discard local checkout; inspect the permanent-loss warning and choose No. Verify the local file and Conflict remain.
7. Close external editors, discard again and explicitly choose Yes. Verify Available at v2; a new checkout must contain v2 bytes.
8. For recovery, use only isolated data: simulate a precommit artifact/staging state as in RecoveryTests, restart or Refresh, and verify restored active edits plus quarantine. A committed v2 artifact must survive reconciliation.

## Phase 5 implementation files

Exact files added or changed from the approved Phase 4 baseline:

```text
README.md
src/DocumentWorkflow.App/App.xaml.cs
src/DocumentWorkflow.App/MainViewModel.cs
src/DocumentWorkflow.App/MainWindow.xaml
src/DocumentWorkflow.App/UserDialogService.cs
src/DocumentWorkflow.Application/CheckInResult.cs
src/DocumentWorkflow.Application/ConflictWorkflow.cs
src/DocumentWorkflow.Application/DocumentWorkflowService.cs
src/DocumentWorkflow.Application/IDocumentWorkflowService.cs
src/DocumentWorkflow.Application/IVersionContentStore.cs
src/DocumentWorkflow.Application/IWorkflowRecovery.cs
src/DocumentWorkflow.Application/IWorkflowStore.cs
src/DocumentWorkflow.Application/IWorkspaceService.cs
src/DocumentWorkflow.Application/WorkingCopyStateService.cs
src/DocumentWorkflow.Domain/Documents.cs
src/DocumentWorkflow.Infrastructure/LocalVersionContentStore.cs
src/DocumentWorkflow.Infrastructure/LocalWorkflowRecovery.cs
src/DocumentWorkflow.Infrastructure/LocalWorkspaceService.cs
src/DocumentWorkflow.Infrastructure/WorkflowStore.cs
tests/DocumentWorkflow.Tests/CheckInTests.cs
tests/DocumentWorkflow.Tests/ConflictTests.cs
tests/DocumentWorkflow.Tests/RecoveryTests.cs
tests/DocumentWorkflow.Tests/WorkflowTests.cs
```

## Phase 6 — version history and workflow timeline

### Baseline and design decision

Implementation began at `3df997e` with a clean working tree, **85 passing tests**, and a build with **0 warnings / 0 errors**. The existing WPF/MVVM → Application → EF Core SQLite/filesystem → Domain architecture is retained. `DocumentVersion` already persisted version ID, document ID, version number, UTC creation timestamp, SHA-256, change note and optional base version; document metadata retained the current pointer and checkout metadata retained the base and creation timestamp. There was no persisted workflow audit model. Logs and deleted checkout rows could not faithfully retain discard or recovery history, so a small additive audit table was justified. It does not drive workflow state and is not event sourcing.

### Version projection and historical inspection

`WorkflowStore.GetHistoryAsync(documentId)` uses exactly **three SQL reads in one transaction**: document/optional checkout, all document versions ordered by version number descending, and document events ordered by UTC timestamp descending with ID as a deterministic tie-breaker. There is no query per version. The application maps each version to `VersionHistoryItem(DocumentVersion Version, bool IsCurrent, string ArtifactPath)` and returns `DocumentHistoryDetails(Context, Versions, Events)`. A checkout is evaluated through the existing canonical hash evaluator. The history query does not perform filesystem recovery or change workflow metadata.

The history window is reached through **View Details → Version history**. Its two separate sections answer which immutable versions exist and which workflow actions occurred. The context banner shows current version, checkout base and evaluated local state; verified Conflict uses the existing warm warning color. Versions show current badges, local display timestamps, 16-character hashes, base versions and honest origins. Only known persisted notes identify initial demo creation, local check-in or the competing writer; other notes show **Not recorded**, while the exact note remains available. Selected metadata exposes full SHA-256, UTC timestamp, artifact path, document/version IDs, base and current/historical status in a read-only, selectable text box. Events have friendly labels and selectable technical details. Both sections scroll; an empty timeline explains that earlier events are not reconstructed.

`OpenVersionAsync(documentId, versionNumber)` validates that the selected version exists in persisted metadata, resolves packaged v1 or managed v2+ storage, creates a unique copy under `workspace/.inspection/{document-id}/v{number}/{inspection-id}/{filename}`, and hashes that copy against the recorded SHA-256 before shell opening. Missing or mismatched content is not opened. No checkout, current-pointer update, audit event or managed-edit state is created. Inspection copies are read-only files and are outside the exact managed working path. Even if an editor later changes an inspection copy, the workflow does not track those changes. Historical artifact and active checkout bytes are preserved. Windows file association is still required.

An open history window refreshes after library workflow actions, Refresh and main-window activation, retaining its document and selected version. Closing the window releases the active history view model. The existing main navigation and workflow actions remain in place.

### Audit model, migration and deduplication

`WorkflowEvent` contains `Id`, `DocumentId`, typed `Type`, UTC `OccurredAt`, nullable `VersionNumber`, `BaseVersion`, `CheckoutAt`, optional short `Details`, and `DeduplicationKey`. Checkout scope uses its existing persisted UTC creation timestamp, since the prior model has no separate checkout ID. Version/base numbers provide context without changing existing version entities. The model stores no serialized application state.

Additive migration **`20261006114618_WorkflowEvents`** creates the table, a unique `(DocumentId, DeduplicationKey)` index, a restricted document foreign key, and SQLite triggers rejecting UPDATE/DELETE. EF SaveChanges also rejects modified/deleted events. Existing versions, IDs, hashes, notes, current pointers and checkouts are preserved. **No events are backfilled.** Fresh first-run document metadata creation is audited once; upgrading an existing database invents no creation events.

| Event | Evidence and deduplication |
| --- | --- |
| DocumentCreated | New first-run document metadata, key `created`; original document timestamp. |
| CheckoutCreated | Successful checkout transaction; key includes checkout timestamp ticks, with base and timestamp scope. |
| CheckInCompleted | Same transaction as new immutable version/current pointer/checkout removal; key contains new version ID, timestamp equals version creation. |
| CompetingVersionPublished | Same transaction as competing version/current pointer; key contains new version ID. |
| ConflictDetected | First successful hash evaluation proving edits against a newer current version; key contains checkout timestamp ticks and current version. Timestamp is observation time, not guessed edit time. |
| CheckoutDiscarded / ConflictDiscarded | Same transaction as authorized discard; verified Conflict selects ConflictDiscarded, otherwise CheckoutDiscarded. Key contains checkout timestamp ticks. Cancelled or rejected discard writes neither event. |
| RecoveryPerformed | A completed staging restore or exact-artifact quarantine move, never merely a missing/locked-file warning. Each actual action has a fresh key and its short result detail. |

Sessions check both pending and committed keys; SQLite writer serialization and the unique index protect cross-instance deduplication. Repeated startup, Refresh, activation and failed stale check-in do not multiply conflict events for the same checkout/current pair. A later current version may produce a new verified observation. **WorkingCopyModified is deliberately not persisted**: refresh-time comparisons cannot establish when an external edit actually happened. The existing Modified state remains hash-derived. Inspection, Keep local edits, export and cancelled confirmations create no audit events.

Authoritative metadata transitions and their events commit or roll back together. Recovery file moves and SQLite are not one physical transaction: a crash or audit-commit failure after a completed move can leave a gap in recovery history. The app preserves recovered bytes and does not fabricate an event on a later no-op refresh. This remains a local audit, not tamper-proof forensic evidence; direct unsupported storage/database changes and machine clock changes are outside its guarantees.

### Verification and desktop acceptance

Final automated result: **113 passed, 0 failed, 0 skipped** — all **85 existing tests** plus **28 Phase 6 cases**. Final build: **0 warnings / 0 errors**. EF reports no pending model changes; the phase diff passes `git diff --check`.

- **History:** v1/v2/v3 ordering, exactly one current badge, persisted IDs after service/store recreation, initial and v2 isolated opening with/without an active checkout, byte/metadata preservation even after inspection edits, rejected unknown/tampered versions, base/current conflict context, honest origins, compact/full hashes and the real Open command.
- **Queries and UI:** three bounded history SQL reads, Details command integration, friendly timeline labels, immediate context/timeline update after confirmed discard, selected metadata, and actual WPF history-window construction/content layout on an STA thread. Desktop testing found and fixed an invalid Auto row-height value; the real-window regression test now covers loading the XAML.
- **Audit:** creation/checkout/check-in timestamps and scopes, competing publication, failed-commit rollback, rejected operations, repeated modified Refresh/activation, conflict deduplication across restart/Refresh/check-in with and without recovery enabled, a newer current version producing a new observation, and no inferred conflict for unchanged/missing/unreadable copies.
- **Discard/recovery/persistence:** cancelled and confirmed normal/conflict discard, real staging restore/quarantine with preserved bytes and no repeated events, warning-only recovery producing no event, cross-document append rejection, duplicate keys within one transaction and after restart, append-only protection through EF and SQL, additive migration preserving legacy data with an empty audit, and idempotent fresh seeding.

Desktop acceptance used disposable temporary data prepared through real application services: checkout v1 → valid XLSX edit → check-in v2 → checkout v2/edit → trusted competing publication v3. The WPF library and history window showed v3/v2/v1, Current, full metadata and one base-v2/current-v3 conflict event. Opening v1 launched Excel **Read-Only** with the original generic catalogue. All three stored hashes, current v3, checkout base v2 and the active local-edit hash remained unchanged. The disposable conflict checkout was discarded through the real workflow service, then desktop activation refreshed the already-open history window to Available / No active checkout and one ConflictDiscarded event. A fresh **desktop Check Out** produced base v3, matching hashes and Unchanged. The test workbook and application were closed afterward. Final discard confirmation via a desktop Yes click was not used; cancellation/confirmation and immediate refresh are covered by actual view-model/service tests. No normal user data was used and no README screenshots were added.

### Phase 6 implementation files and commits

Files added/changed relative to `3df997e`:

```text
README.md
src/DocumentWorkflow.App/Commands.cs
src/DocumentWorkflow.App/HistoryViewModel.cs
src/DocumentWorkflow.App/HistoryWindow.xaml
src/DocumentWorkflow.App/HistoryWindow.xaml.cs
src/DocumentWorkflow.App/MainViewModel.cs
src/DocumentWorkflow.App/MainWindow.xaml
src/DocumentWorkflow.App/MainWindow.xaml.cs
src/DocumentWorkflow.Application/ConflictWorkflow.cs
src/DocumentWorkflow.Application/DocumentWorkflowService.cs
src/DocumentWorkflow.Application/HistoryWorkflow.cs
src/DocumentWorkflow.Application/IDocumentWorkflowService.cs
src/DocumentWorkflow.Application/IWorkflowRecovery.cs
src/DocumentWorkflow.Application/IWorkflowStore.cs
src/DocumentWorkflow.Domain/WorkflowEvent.cs
src/DocumentWorkflow.Infrastructure/AppDbContext.cs
src/DocumentWorkflow.Infrastructure/DatabaseInitializer.cs
src/DocumentWorkflow.Infrastructure/LocalWorkflowRecovery.cs
src/DocumentWorkflow.Infrastructure/Migrations/20261006114618_WorkflowEvents.Designer.cs
src/DocumentWorkflow.Infrastructure/Migrations/20261006114618_WorkflowEvents.cs
src/DocumentWorkflow.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
src/DocumentWorkflow.Infrastructure/WorkflowStore.cs
tests/DocumentWorkflow.Tests/AuditTests.cs
tests/DocumentWorkflow.Tests/CheckInTests.cs
tests/DocumentWorkflow.Tests/HistoryQueryTests.cs
tests/DocumentWorkflow.Tests/HistoryTests.cs
tests/DocumentWorkflow.Tests/RecoveryTests.cs
tests/DocumentWorkflow.Tests/WorkflowTests.cs
```

Logical implementation commits:

- `d181166` — feat(history): query versions and open verified isolated inspection copies
- `af5312e` — feat(audit): persist append-only workflow transitions and verified observations
- `6f874e7` — feat(ui): present version history and document workflow timeline
- `00879d3` — test(history): verify immutable inspection and details presentation
- `8c8f34c` — test(audit): cover scoped transitions recovery deduplication and rollback
- `bf84379` — fix(ui): load automatic timeline row heights in the WPF history window
- `c672ea0` — fix(audit): deduplicate pending events within a workflow transaction

The final documentation commit is recorded in Git history. No new repository, checkout root or architecture was introduced.

### Limitations and Phase 7 recommendation

Older audit actions are unavailable rather than inferred. Initial v1 remains the packaged source, with inspection hash checking to detect unexpected replacement. Inspection copies accumulate locally; cleanup/retention is deferred. Audit history is intentionally unpaged for this small catalogue, with no event filters or search subsystem. Checkout correlation uses the existing creation timestamp rather than a durable independent operation ID. External editors, clock changes and direct unsupported storage changes remain outside the app's control. Recovery audit has the filesystem/database gap described above.

**Stop after Phase 6 for review.** Recommended Phase 7 scope, only after approval: choose Windows packaging/publish strategy, repeatable clean-machine installation/upgrade validation, backup/migration guidance, then release automation and portfolio documentation. No installer, publishing, CI/CD, release, watcher, remote backend, cloud sync, branding or auto-update work is implemented in Phase 6.
