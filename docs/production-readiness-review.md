# Production readiness review — 23 September 2026

> **Status:** historical record. The five findings below were fixed and independently rechecked (see the end of this page); the fixes are committed and ship in 0.7.7. References to uncommitted work and to `artifacts/` evidence files describe the reviewer's local working tree at the time and are not part of the repository. Remaining release acceptance work is tracked in [implementation status](implementation-status.md). Since this review, value redaction was removed (0.7.20/0.8.3) and official releases are Authenticode-signed (0.8.5); GitHub CI remains absent. The body below is unchanged.

Verdict: not ready for an unrestricted production release. Suitable for a controlled development pilot after the high-priority preparation/restart issues are resolved. This assessment covers the current working tree, including the uncommitted preparation implementation; it does not certify the previously installed/public release.

## Findings

### P1 — Desktop permits unrelated requests while a previous preparation remains applied

Location: src/E2ETester.Desktop/MainViewModel.Preparation.cs:131–136.

EnsurePreparationReady rejects pending writes but returns immediately for a case without preparation. An applied preparation belonging to another case/package does not block it. Moving from a restricted-permission scenario to an ordinary case can therefore send under modified database state and the restricted token, producing misleading test results. MCP explicitly rejects unrelated pending preparations; desktop behavior differs.

Confirmed with an isolated reflection probe against the actual desktop model: insert an applied recovery record for another case and invoke the execution guard for an ordinary case. The guard accepts it. No external service or database was contacted.

Required change: restore or block before executing a case outside the active preparation's scope. Bind preparation ownership to the run, and test navigation, new run, package switching and resuming historical runs.

### P1 — Update restart loses the workspace for double-clicked packages

Locations: src/E2ETester.Desktop/UpdatesViewModel.cs:98–99; src/E2ETester.Desktop/App.xaml.cs:43–48.

Opening a .jsee file selects a workspace derived from its absolute path. Update restart retains only explicit --workspace and --dark arguments, discarding the .jsee argument. An app launched by file association therefore restarts in the default workspace instead of the package workspace. The saved run, draft and preparation recovery remain on disk but are no longer the workspace being opened.

Confirmed the argument transformation in a local probe; actual installed upgrade was not performed during this review.

Required change: restart with the resolved current workspace explicitly, and test installation/update launched from a .jsee file.

### P1 — Update/rollback preflight does not restore active external changes

Location: src/E2ETester.Desktop/MainViewModel.Diagnostics.cs:43–49.

The update preflight saves the draft and backs up the SQLite journal, but does not call RestorePreparationsAsync or reject active/unreadable preparation recovery. Normal user-driven exit does restore. The update path must not depend on ordinary window closing to provide this guarantee. Rollback to a version without preparation support is particularly problematic: that version cannot offer recovery for external changes left applied. The SQLite backup does not include the separate encrypted recovery journal.

Confirmed by code review. No live installation or database mutation was performed to reproduce rollback.

Required change: complete and verify restoration before invoking the updater; abort on conflict or unreadable recovery. Add an installed upgrade/rollback test with active preparation and verify the original database value afterward.

### P2 — Reports do not identify the actual preparation value selected by the tester

Locations: src/E2ETester.Core/PreparationModels.cs:39; src/E2ETester.Desktop/MainViewModel.Preparation.cs:78–113.

The Prepare dialog allows an execution-time value/flag change. That changed plan lives in recovery storage, while the package retains its baseline plan. PreparationAudit records only timestamp, case ID, title and status. A report therefore cannot distinguish an applied value of 4 from a tester-selected value of 8 using its preparation audit. For API preparations, it also lacks the concrete mutation request/response evidence. This weakens the intended AI-readable and reproducible test record.

Required change: persist an immutable, appropriately redacted preparation execution record with actual target/value, identity, outcome, restoration outcome and correlation ID. Keep sensitive original values protected. Test execution-time flag changes against exported reports.

### P2 — Preparation requests bypass ordinary request validation

Locations: src/E2ETester.Application/Preparations.cs:15; src/E2ETester.Application/PackageJson.cs:78.

Ordinary test requests enforce a 1–600 second timeout. Nested preparation capture/update requests do not receive the equivalent request validation. A probe confirmed Preparations.Validate accepts TimeoutSeconds = -1; HttpTransport then encounters an invalid CancelAfter time. Other nested transport fields also need shared validation rather than a separate partial validator.

Required change: extract and reuse complete request validation for both ordinary and preparation requests. Verify bad methods, headers, URLs, timeouts and body formats fail during package validation, before any external mutation.

## Verification performed

- Release solution build: succeeded, zero warnings/errors.
- Integration suite: 309 checks passed.
- Actual MCP protocol test: passed, 23 tools discovered, including preparation tools.
- Desktop feature smoke in dark mode: passed.
- Git-visible publication audit: 339 files passed configured private-content and credential-pattern checks. This is a pattern audit, not proof that every possible secret is absent.
- NuGet direct/transitive vulnerability query against the configured nuget.org source: no known vulnerable packages reported for all six projects at review time.
- Current locally published MCP executable: Authenticode status NotSigned.
- Targeted isolated probes confirmed the desktop guard and update-argument findings plus missing preparation timeout validation.

Evidence: artifacts/readiness-tests.log, artifacts/readiness-probe-results.txt, artifacts/readiness-dark.png and its binding log. The probe source is confined to ignored artifacts/readiness-probe. Application source was not modified as part of this review.

## Remaining release validation

After fixing the findings, run fresh installed-app tests on a clean Windows user profile: install, file association launch, update, rollback, offline startup, interrupted preparation and recovery, and fresh-login permission scenarios. Repeat database integration checks across all four providers for the fixes. This review did not rerun the container database matrix or authenticate against a real external provider.

Establish a repeatable required release gate for build, tests, publication audit and package checks. GitHub CI is currently absent because it was intentionally removed earlier; a documented local release gate is acceptable until CI is restored. Production distribution should include an explicit signing decision; the inspected binary is unsigned. Build a uniquely versioned release from a reviewed commit after fixes; the current work is uncommitted and still identifies as 0.7.7.

No live API calls, permission changes, database writes outside disposable test infrastructure, deployment, commit or push were performed for this review.

## Remediation — 24 September 2026

The five findings above have code fixes in the working tree:

- Desktop preparation is bound to its run as well as package, case and base URL. The guard executes before sign-in or sending and rejects unrelated cases, new runs and reopened packages until restoration. Existing recovery entries without run ownership require restoration.
- Update and rollback restart with the model's resolved absolute workspace and current theme, including file-association launches.
- Version-change preflight restores and verifies preparations before saving the draft and backing up the workspace. Conflicts, unreadable recovery and failed journal persistence abort the operation. A failed persistence write keeps the in-memory recovery pending for retry.
- Append-only preparation audit snapshots include actual selected values, target plan, identity alias, run/operation correlation, and restoration outcomes. API evidence includes request method, resolved URL, status and outcome; the actual redacted mutation template is included in the execution snapshot. Original scalars and API response bodies are excluded from reports.
- Ordinary and nested preparation requests share transport validation, including timeouts, method, URL, headers, proxy and body formats. The report validator now accepts schema 1.8.

Validation: Release solution build passed with zero warnings/errors; 324 integration checks passed; MCP protocol tests passed with 23 tools; desktop feature smoke passed (exit 0), including unrelated-case/run/reopen guards, restart arguments, successful preflight restoration, conflict and unreadable-recovery rejection; schema 1.8 exported preparation report validated; publication audit passed for 340 Git-visible files. Evidence: artifacts/readiness-fixes-tests.log and artifacts/readiness-fixes-final.png with its empty binding log. Schema validation used an isolated ignored Python environment under artifacts.

Release status remains conditional: no installed-app upgrade/rollback, real authentication, or four-provider database matrix was performed during this remediation. Database mutations were confined to disposable SQLite test databases. No release was installed, published, committed or pushed. The installed-app and real-authentication release checks listed above remain required.

## Independent recheck — 24 September 2026

Rechecked the current working-tree fixes against all five original findings. No remaining blocker was found in those reviewed paths. The original findings above are historical; their code fixes are verified by this recheck.

- Release build passed with zero warnings/errors.
- 324 standard integration checks passed.
- Full database run passed 426 checks, including SQLite and disposable SQL Server, MySQL and PostgreSQL infrastructure. The unique Docker test project's containers/volumes/network were cleaned up.
- Actual MCP protocol tests passed with 23 tools.
- Desktop feature smoke passed with zero binding-log output, covering case/run isolation, reopened-package recovery, fresh-token checks, update workspace arguments, restoration preflight, conflict rejection and unreadable recovery.
- Exported preparation report passed schema 1.8 validation.
- Publication audit passed for 340 Git-visible files; diff whitespace checks passed.

Evidence: artifacts/readiness-recheck-tests.log, artifacts/readiness-recheck-databases.log, artifacts/readiness-recheck.png and artifacts/readiness-recheck.png.bindings.log.

Verdict: ready for release-candidate acceptance testing, not yet fully verified for production deployment. Remaining acceptance work is a real installed-app install/update/rollback cycle (including file-association launch) and real-provider authentication/re-login. Those were not performed in this recheck. Current fixes remain uncommitted and the source version remains 0.7.7; release packaging/versioning and the previously noted signing decision remain separate release tasks. No application source, live permissions, or live service data was changed during this recheck.
