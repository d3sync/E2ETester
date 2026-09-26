# Implementation status — 0.8.0

The local platform is implemented across Core, Application, Infrastructure, Desktop, Mcp, Cli and IntegrationTests. Version history is in the [changelog](../CHANGELOG.md); this page summarizes what exists now and what still needs a real deployment to confirm.

## Implemented

- Guided three-column WPF runner with editable required fields, original-value labels, variations, round PASS/FAIL/INCONCLUSIVE status badges, navigation, saved runs and an explicit finish/continue choice.
- `.jsee` packages (package schema 1.4, or 1.5 with preparation) with backward import of 1.0–1.3, file association, case authoring, dependency protection, library snapshots and redacted recovery including unfinished Query Studio edits.
- Text/JSON/XML/form/multipart/binary HTTP, file upload/download, isolated cookies, cross-case typed captures with source-attempt provenance, timeout/cancel, no automatic retries, recorded same-origin redirects, HTTP version preference, proxy and local client certificates.
- Status/header/body/JSON/numeric/array/duration assertions, truthful PASS/FAIL/INCONCLUSIVE results and separate manual assessments tied to an evidence hash.
- Swagger/WebView2 token import with persistent browser profiles, browser OIDC/PKCE with signed identity validation and serialized refresh, manual JWT/API tokens and named identities. Optional Windows DPAPI storage for environments and remembered sessions.
- SQL Server/MySQL/PostgreSQL/SQLite Query Studio with typed parameters, fixtures, response bindings, comparison/existence checks, bounded polling, restricted read-only execution, limits and cancellation.
- Optional preparation: exact database-value or API changes applied before a case, with captured originals, fresh-login support, conflict-aware restoration and an audit trail in reports.
- Durable pre-send journal, exclusive workspace ownership, restart recovery without replay, immutable finished-run execution.
- JSON reports (schema 1.7, or 1.8 with preparation), readable report modal, safe standalone HTML and ZIP evidence bundles with SHA-256 manifests.
- API workspace for ad-hoc requests with Swagger 2.0 / OpenAPI 3.x JSON import.
- Official-SDK MCP stdio host with 23 tools covering scoped targets, runtime permissions, exact approved SQL and preparation plans, browser sign-in, the package library, shared execution/checks/reports, cancellation and durable duplicate-call protection. An AI prompt, package template and skill kit are included.
- Consistent backups, explicit finished-run cleanup, disk/integrity diagnostics and no automatic deletion.
- Self-contained single-file Windows x64 executables plus a Velopack installer with update checks, version selection and rollback.

## Verification

See [testing](testing.md). The integration suite runs against loopback HTTP, a synthetic signed OIDC provider, SQLite and Windows DPAPI. `Test-Databases.ps1` adds disposable SQL Server, MySQL and PostgreSQL containers. `tools/test_mcp.py` drives the real MCP stdio host. Desktop `--smoke` modes render and exercise the WPF journeys and fail on binding errors. `tools/check_publication.py` audits Git-visible files before release.

## Not yet validated against a real deployment

- Registration of a native OIDC client with a real identity provider, real MFA and account switching.
- End-to-end authenticated API and database pilots in a real environment.
- Clean-machine Windows installation, enterprise proxy/mTLS targets and installed upgrade/rollback from a file-association launch.
- Installer and executable signing. Distribution is currently unsigned.

## Out of scope for now

Remote MCP transport and arbitrary writable setup/teardown SQL. Query Studio remains read-only. Group operations and versioned autosave history are not planned for the initial release.

See [MCP setup](mcp.md), [HTTP workflow](http-workflow.md), [workspace and distribution](workspace.md) and the [production readiness review](production-readiness-review.md).

Version 0.8.0 adds Postman 2.1 migration with warnings, OpenAPI YAML/supplied reference documents, JSON Schema response assertions and a headless runner with JUnit. See [imports and CI](imports-and-ci.md). The [first-run human study](first-run-study.md) is prepared but not yet conducted.
