# Document Workflow Desktop

A .NET/WPF desktop application demonstrating document checkout, local editing, version tracking, conflict handling and reliable check-in workflows.

## Overview

This fresh, generic public implementation is being built in incremental, tested milestones. **Phase 1 is the library foundation.** Checkout, editing and check-in are roadmap features, not implemented behavior.

The current application launches a restrained WPF document library backed by a local SQLite database. Demo entries contain metadata only; no Word, Excel or PDF content files are supplied.

## Features

Implemented in Phase 1:

- Document library loaded from SQLite, sorted by logical name.
- File name, version, status and local-time update timestamp.
- Refresh command with loading, empty and failure messages.
- Inline document metadata details with a Close command.
- Five generic demo documents seeded once into an empty database, with initial version metadata.
- EF Core migration for documents, versions and working-copy metadata.
- Domain validation, DI, generic hosting, configuration and structured local JSON logs.
- Automated domain, SQLite persistence, seed and MVVM behavior tests.

Working-copy records are a persistence foundation only. Demo documents remain Available at version 1. Seed hashes identify metadata fixtures and are explicitly not content-file hashes.

## Architecture

```text
DocumentWorkflow.sln
src/
  DocumentWorkflow.Domain          Entities, defaults and validation; no UI or database dependencies
  DocumentWorkflow.Application     Document repository contract
  DocumentWorkflow.Infrastructure  EF Core SQLite context, migration, repository and initializer
  DocumentWorkflow.App             WPF views, view models, commands and DI composition root
tests/
  DocumentWorkflow.Tests           xUnit domain, persistence and MVVM tests
```

Dependencies flow from App to Infrastructure to Application to Domain. The App composes services at startup, applies migrations, seeds an empty library, opens the window and loads document metadata. Each repository read creates and disposes its own context and uses no-tracking queries. View models expose commands and state; the window's code-behind only initializes the view and attaches its view model.

Version numbers are unique per document. Versions and working-copy metadata have foreign keys to documents. Demo initialization uses a transaction and does not add duplicate records on restart. Startup failure displays an error; refresh failures preserve the displayed library and allow retry.

## Tech Stack

- .NET 9 and C#
- WPF and MVVM
- SQLite and EF Core 9
- Microsoft.Extensions.Hosting, configuration, dependency injection and logging
- xUnit

## Getting Started

Requirements: Windows and the .NET 9 SDK. Visual Studio is optional; if used, install the .NET desktop development workload.

From the existing repository root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DocumentWorkflow.App
```

Or open `DocumentWorkflow.sln` and select `DocumentWorkflow.App` as the startup project.

### Local data and logs

By default the application stores the database at:

```text
%LOCALAPPDATA%\DocumentWorkflowDesktop\documents.db
```

Daily JSON-lines logs are stored under the adjacent `logs` directory. Log events include timestamps, categories, named properties and exception information. Logs are local and currently have no automatic retention policy.

The host supports a data-directory override without an appsettings file:

```powershell
dotnet run --project src/DocumentWorkflow.App -- --DocumentWorkflow:DataDirectory C:\Temp\DocumentWorkflowDemo
```

Alternatively set the `DocumentWorkflow__DataDirectory` environment variable. Use an absolute directory path. Future checked-out content will use a dedicated filesystem workspace; Phase 1 does not create working files.

Migrations apply automatically at startup. For development migration commands, restore the repository-local tool:

```powershell
dotnet tool restore
dotnet ef migrations list --project src/DocumentWorkflow.Infrastructure
dotnet ef migrations add MigrationName --project src/DocumentWorkflow.Infrastructure
```

The design-time context uses `document-workflow.db` in the command's current directory. It is separate from the application's default runtime database. Do not run migration updates against the design-time database expecting them to change runtime data.

### Manual acceptance check

1. Launch the application and verify that the library shows five documents: Product Catalog, Supplier Agreement, Installation Guide, Pricing Overview and Technical Specification.
2. Verify each entry shows its original file name, v1, Available and an update timestamp.
3. Click Refresh repeatedly; the list should remain at five entries.
4. Open View Details for different documents and close the details panel; metadata should match the selected row.
5. Resize the window and use keyboard navigation to reach Refresh and document actions.
6. Close and reopen the application; data and document identifiers should persist, with no duplicate seed rows.
7. Check the local logs for startup and document-count events.

Automatic verification covers domain defaults and invalid input, migration-backed document/version/working-copy round trips, unique version constraints, repository reads, file-database restart and seed idempotency, details commands, empty results and refresh retry after failure. Launch smoke checks also confirmed that the WPF window opens and five SQLite documents load on first start and restart. Visual layout and keyboard usability still need manual acceptance.

## Roadmap

After Phase 1 manual acceptance:

- Phase 2: real generic demo content, filesystem workspace and checkout/discard with tested failure handling.
- Later: external editing and file-change detection.
- Later: check-in and version history.
- Later: conflicts and explicit resolution.
- Later: recovery and audit visibility.

No checkout, file watcher, check-in, conflict resolution or recovery implementation is included in this phase.

## Screenshots

Pending manual review of the Phase 1 library. No screenshots are included yet.
