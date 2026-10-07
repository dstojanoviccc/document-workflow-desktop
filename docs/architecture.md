# Architecture and persistence invariants

The existing WPF/MVVM → application → infrastructure/domain structure is retained. The application coordinates interfaces; infrastructure supplies EF Core SQLite, file hashing, workspace/version storage and Windows file opening. Domain entities validate transitions without depending on WPF or EF.

SQLite owns document identity, current version, immutable version metadata, checkout base/baseline and typed audit events. Managed storage owns working copies, immutable content, staged operations and quarantine. A file alone does not prove a committed version: database references are authoritative.

## Important invariants

- A published version's bytes and hash do not change. Check-in creates another artifact.
- Checkout records its base version and baseline SHA-256 persistently.
- Working-state comparison uses bytes, not last-write timestamps.
- SQLite metadata transitions and their normal workflow audit events commit atomically.
- Check-in conditionally advances the version only when current equals checkout base. A stale base cannot check in.
- Conflict detection preserves the working copy. Inspection uses an isolated verified copy; export does not silently end checkout.
- Recovery respects committed references and does not erase unknown content.
- Audit is append-only through the workflow store. Legacy actions are not backfilled with fabricated events.

## Delivery boundary

`RuntimePaths` preserves the existing LocalApplicationData root independently of installation location and rejects writable data/workspace paths inside the executable directory. Packaged demo files are read-only inputs; they load only by an explicit empty-state action. Initialization applies migrations without reseeding existing records.

`Directory.Build.props` supplies the single application version. The publish profile supplies the self-contained Windows target; the installer receives its version from the packaging script. No business workflow changes were introduced for delivery.

See [implementation history](implementation-history.md) for detailed component descriptions and previous phase evidence, and [recovery](recovery.md) for cross-store failure handling.
