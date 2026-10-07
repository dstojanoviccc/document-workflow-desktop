# Evaluation workflow

Fresh startup initializes SQLite and required directories, then shows an empty library. **Load demo data** explicitly adds five generic samples; repeating initialization does not duplicate them. Existing user state is never automatically seeded.

## Normal editing

Check Out creates a managed copy and records the current version as the checkout base. Open uses the default Windows file association. Edit/save externally, then return or Refresh. Identical bytes remain Unchanged; changed bytes become Modified. Missing/unreadable working files are visible problems, not silently recreated content.

Check in verifies a Modified working copy and its base, publishes a new immutable artifact and atomically advances SQLite metadata. Checkout completes. View Details lists the version history and audit timeline. Opening a historical version verifies its hash and creates an isolated inspection copy; modifying that copy cannot change the committed version.

## Conflict demonstration

Competing publication is available to services/tests, not as an artificial public UI action. A checkout based on v2 with local edits becomes Conflict when another writer publishes v3. Check-in is blocked. The user may keep local edits, save a copy, inspect latest, or explicitly confirm discarding the checkout. There is no force overwrite or automatic merge.

Run the complete suite for concurrency examples:

```powershell
dotnet test DocumentWorkflow.sln -c Release
```

Concurrency and conflict-recovery tests demonstrate these scenarios without requiring two installed applications. The screenshots use generic sample state prepared through the same services.
