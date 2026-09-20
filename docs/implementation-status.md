# Implementation status — 0.7.0

Updated 2026-09-20. The local platform is implemented across Core, Application, Infrastructure, Desktop, Mcp and IntegrationTests. Live Example and deployment acceptance are separate gates.

## Version 0.6.0 additions

Compact authentication, optional advanced database/transport controls, parameter/header deletion, stable tab hover templates, and an API-only Cloud test shortcut. Velopack packages support explicit version selection and rollback with a saved draft and pre-update workspace backup. MIT licensing, contributor/security guides, architecture documentation and branching-timeline branding are included. Public feed hosting and signing are not configured.

## Implemented

- Guided three-column WPF runner, editable required fields, original-value labels, variations, navigation, saved runs and explicit finish/continue choice.
- Package schema 1.2 with backward import of 1.0/1.1; case authoring, dependency protection, library snapshots and redacted recovery including unfinished Query Studio edits.
- Text/JSON/XML/form/multipart/binary HTTP, file upload/download, isolated cookies, cross-case typed captures with source-attempt provenance, timeout/cancel, no automatic retries, recorded same-origin redirects, version preference, proxy and local client certificates.
- Status/header/body/JSON/numeric/array/duration assertions and truthful PASS/FAIL/INCONCLUSIVE results.
- Browser OIDC/PKCE with signed identity validation, independent sessions and serialized refresh; manual JWT/API tokens. Optional Windows DPAPI environment storage; browser tokens remain in memory.
- SQL Server/MySQL/PostgreSQL/SQLite Query Studio, typed parameters, fixtures, response bindings, comparison/existence checks, bounded polling retaining every observation, restricted reads, limits and cancellation.
- Durable pre-send journal, exclusive ownership, restart recovery without replay, immutable finished-run execution and separate manual assessments.
- Report schema 1.5, typed evidence pointers, safe standalone HTML, ZIP evidence bundles with SHA-256 manifests.
- Consistent backups, explicit finished-run cleanup, disk/integrity diagnostics and no automatic deletion.
- Official-SDK MCP stdio host with 20 tools: scoped targets, runtime permissions, exact approved SQL, browser sign-in, package library, shared execution/checks/reports, cancellation and durable duplicate-call protection.
- Appropriate .gitignore and self-contained Windows x64 distribution script for both hosts with docs, samples, schemas and integrity manifest.

## Verification

Version 0.6.0 additionally passes isolated real Velopack upgrade/rollback and restart checks, light/dark usability checks, close/save-dialog checks, Swagger popup/import checks, browser-process restart persistence checks, and MCP protocol checks. The WPF checks emitted no binding warnings. The older version in the update test is a test-only build; this does not establish schema compatibility with arbitrary historical releases.

Release builds pass without warnings/errors. The latest local suite passes 244 checks against loopback HTTP, synthetic signed OIDC, SQLite and Windows DPAPI. This includes real multipart/binary bytes, cookie isolation, capture provenance, redirects, polling evidence, duplicate execution across restart, exact query approval, backup consistency, protected storage and report hashes.

Another 96 checks pass against isolated SQL Server, MySQL and PostgreSQL containers, covering types/nulls, read-only accounts, limits, cancellation and recovery. The combined run passed 313 checks before five final edge-case checks were added. Test containers/volumes/network were removed.

Real MCP stdio initialization, 20-tool discovery, calls and safe errors pass. WPF HTTP, database, history, authentication failure, authoring, manual-assessment and workspace/profile journeys pass without binding errors. Screens were rendered and inspected. Desktop database/assessment reports independently validate against schema 1.4 and their evidence pointers. Bundle tests verify all file hashes and lengths.

## External gates and scope

Example native-client registration, real MFA/account switching, public API-token format and approved API/DB business pilot remain unverified. The cloud sample has been checked without authentication; authenticated company API/database acceptance remains outstanding. Environment URLs and identity aliases are needed; do not provide credentials in chat.

Clean-machine Windows acceptance, enterprise proxy/mTLS target testing, installer signing and organization-specific deployment remain external release gates. Published-binary testing on this workstation cannot establish clean-machine acceptance. Distribution is unsigned.

The plan deferred OpenAPI import, JSON Schema assertions pending library selection, remote MCP transport and optional writable setup/teardown. Query Studio remains read-only. Group operations and versioned autosave history are outside the initial release.

See [completion checklist](completion-checklist.md), [MCP setup](mcp.md), [HTTP workflow](http-workflow.md) and [workspace/distribution](workspace.md).

Published Windows executables also passed the workspace/profile/draft smoke and a real MCP workflow: package library, two API attempts including a variation, duplicate delivery protection, finish and canonical report. All 733 release-file hashes verified. The ZIP includes .NET and Windows Desktop runtime 10.0.10 and was tested on this workstation.

## 0.5 additions

Embedded persistent Edge WebView2 Swagger browser captures same-origin Bearer headers or Swagger authorized access tokens, with explicit import. No client secret is required for this supplied-token path. Local WebView2 capture/import checks pass; live authenticated sign-in is pending user interaction. Prepared request snapshots and redacted cURL persist in attempts and report 1.5, including redirect hops and body bytes. GitHub-inspired light/dark themes apply across desktop controls. A real unauthenticated gateway GET to `/whoami` returned 401; the reported authenticated 404 remains to be reproduced with the user’s token.

### 0.5.1 browser correction

Authorization popups now receive a separate WebView2 using the same environment/profile, preserving window.opener and callback messages instead of replacing Swagger. Stable named profiles retain browser cookies and site storage according to website lifetime policies. Back, Reload, Clear browser session and navigation-failure messages are available. Real local WebView2 regression tests cover popup redirect/callback/closure and browser-session reopening. Live Example sign-in awaits user retry.

### 0.5.2 remembered sign-in

Native WebView2 Save/Update Password prompting is enabled. Separate app and Chromium process tests prove persistent cookie/site-storage retention and explicit clearing. Microsoft session reuse remains governed by provider/tenant policies; no guarantee of skipping MFA is made. Both published hosts remain self-contained single-file executables.

### 0.5.3 branding

Generated endpoint/check artwork, Windows multi-resolution icons, light/dark SVG wordmarks and a visual brand guide are in assets/branding. Both executables embed the icon; WPF uses the artwork in its header and window icon. Light/dark smoke renders passed without binding errors, and Windows extracted the executable icon successfully. Single-file publishing is retained.

### 0.5.4 cloud package and exit protection

Cloud checkremoteip package with three read-only requests, embedded for --auth-check and exported as JSON. Non-empty text assertions reject empty, whitespace, binary and incomplete responses appropriately. Explicit-save dirty tracking covers package and query edits; the close prompt supports save/cancel/exit without final save and blocks close during active operations. The actual WPF Cancel and Save-and-exit paths and restored drafts pass smoke checks. Local suite: 244 checks passed. Live unauthenticated gateway baseline: 401; authenticated success awaits user sign-in. Revised branding separates the dark check from the green path.

## Version 0.7 verification

265 integration checks pass, including authentication-only journaling, setup ordering and OpenAPI parsing. Published .jsee shell opening (including spaces), modal report rendering, MCP initialization/template validation, skill installation and token environment forwarding were verified. UI feature, close/save and Swagger checks emitted no binding warnings. OpenAPI currently accepts JSON only; advanced serialization and remote refs require manual handling.
