# Release candidate validation

Validation completed on October 7, 2026, on the existing Windows development machine. No second clean Windows VM was available. This record distinguishes local evidence from unexecuted remote checks.

## Build and packaging

The original repository targeted .NET 9 / WPF, had 113 passing tests, zero build warnings/errors and no publish profile, installer, authoritative release version or GitHub Actions workflows. User state already lived under LocalApplicationData; that location is preserved.

The final candidate uses authoritative version **1.0.0**, Release / `win-x64`, self-contained .NET runtime **9.0.20**, Inno Setup **6.7.3** and a ZIP alternative. Directory publishing avoids trimming and single-file assumptions around WPF/native SQLite. The packaging script generates two versioned packages plus SHA-256 sums under ignored `artifacts/release/1.0.0`.

| Check | Local result |
| --- | --- |
| Restore | Succeeded |
| Debug build / tests | Zero warnings/errors; 121 passed, zero failed/skipped |
| Release build / tests | Zero warnings/errors; 121 passed, zero failed/skipped |
| Publish, ZIP and installer compilation | Succeeded through `scripts/Build-Release.ps1` |
| Version metadata | Assembly/file/product, UI and installer agree on 1.0.0 |
| EF model/migration consistency | No pending model changes using the infrastructure design-time factory |
| Dependency advisory scan | No known vulnerable direct/transitive packages reported by NuGet |
| Workflow YAML and PowerShell | Both YAML files and all run snippets parsed successfully |

Eight meaningful delivery test cases were added: default data-path independence, three invalid installation-state path cases, version metadata consistency, explicit/idempotent demo loading, incomplete demo-package protection, and preservation of existing conflict/version/audit state through startup. All 113 prior cases remain green.

## Installation lifecycle evidence

The test installation used an isolated binary directory and an isolated data root, leaving the normal user-data root untouched. The installer identity had no pre-existing application registration before testing.

1. **Fresh install:** the actual per-user installer completed; registration reported 1.0.0. The app launched, created SQLite and required directories and showed an empty catalogue. Optional Load demo data added five valid generic samples.
2. **Bundled runtime:** the installed process loaded `coreclr.dll` from its installation directory. The ZIP/publish output includes WPF and native SQLite. This verifies bundled-runtime use on this machine, not a machine without any SDK/runtime installed.
3. **Realistic state and restart:** actual application services checked out v1, edited a valid XLSX, checked in v2, checked out again and retained local edits. Competing publication produced current v3 versus checkout base v2. Relaunch retained Conflict. Snapshots compared document/version/event IDs, checkout timestamp, working-file hash, version hashes and retained recovery/inspection/log content.
4. **Upgrade:** a validation-only 1.0.1 package, compiled from the same implementation with a version override and the same installer identity, replaced installed 1.0.0. The UI displayed 1.0.1 and Conflict. The exact snapshot remained equal: current v3, base v2 and six audit events. The authoritative repository version stayed 1.0.0.
5. **Uninstall:** the uninstaller removed the app executable and uninstall registration. Surviving SQLite/workspace/version/audit/recovery/inspection content matched the snapshot.
6. **Reinstall:** installing 1.0.0 again launched against the retained data without reseeding or resetting it. The same snapshot remained equal.

This was a binary/version upgrade with unchanged schema. Existing migration/startup paths are covered by real SQLite tests; a future schema downgrade is not implied by this experiment. The retained recovery fixture checked preservation, while automated recovery tests exercise interrupted operations and quarantine decisions.

## Public presentation

Three real application captures are stored in `docs/assets`: `modified.png`, `conflict.png`, and `history.png`. Main/recovery captures use the same window size; the existing history window uses its own dimensions. All are lossless PNGs, together under 300 KB. They show generic document names, Modified state, Conflict recovery controls, v1–v3, the Current indicator and typed audit events. Visible fixture paths use a generic evaluation directory without a username or OneDrive path. No unrelated desktop content was saved.

The screenshot helper initially returned inconsistent foreground captures and stale elements. Resetting its session recovered capture; only images inspected as the intended application were saved. Conflict state was prepared through application services, not fabricated UI or a new production feature.

## Security and repository hygiene

Source/docs/config scans found no embedded credentials, token patterns, personal machine paths, private/client project names or unexpected runtime files. A history content scan found no matching private-path/token patterns. This is a targeted scan, not a guarantee that every possible secret can be detected.

Existing Git author metadata contains a personal author email, alongside earlier GitHub noreply identities. It was not rewritten. The configured repository identity was retained for new commits. Generated `bin`, `obj`, installer/publish output, SQLite files, logs, workspace and recovery content remain ignored. The five original Office/PDF samples are intentional tracked binaries.

There is **no LICENSE**. Selection remains the owner's decision; this pass does not grant or add a license.

## Verification limits and publication boundary

- No clean second Windows VM or runtime-free machine was tested.
- GitHub-hosted CI and tag-triggered release upload were not executed. Local equivalent build/test/package steps and workflow syntax were verified.
- Packages are unsigned; SmartScreen reputation and publisher trust were not established.
- No tag, push, GitHub Release or public artifact upload was performed.
- .NET 9 support ends November 10, 2026; framework servicing/upgrade remains a near-term maintenance task.

The repository is locally ready for the owner to review and tag **v1.0.0**. Licensing is the remaining owner decision. A successful remote CI run should precede publication; optional clean-machine and signing checks would strengthen distribution evidence.
