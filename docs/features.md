# Feature overview

| Area | Available |
| --- | --- |
| Guided tests | Sequential cases with PASS/FAIL/INCONCLUSIVE status badges, custom variations, editable defaults, required/optional fields, parameter/header deletion |
| HTTP | Standard/custom methods, text/JSON/form/multipart/binary bodies, cookies, bounded responses, same-origin redirect support |
| Authentication | Swagger/WebView2 token import, manual JWT/API tokens, registered native OIDC/PKCE, multiple identities |
| Assertions | Status, headers, non-empty/substring text, JSON values/presence/comparisons/array counts, duration |
| Database | SQL Server, MySQL, PostgreSQL, SQLite; read-only queries, typed parameters, fixtures and post-request verification |
| Evidence | Durable attempts, complete captured values, prepared request/cURL, response/history, manual assessments, JSON/HTML/ZIP reports |
| Workspace | Recovery drafts, save-on-exit choices, library, encrypted environments, backups and retention controls |
| Preparation | Optional, host-approved setup (database value or API change) before a case, with captured originals and verified restoration |
| API workspace | Ad-hoc requests, Swagger 2.0 / OpenAPI 3.x JSON import, cURL evidence |
| Automation | MCP stdio tools with explicit target/query/authoring/preparation permissions and idempotency; AI prompt, template and skill kit |
| Appearance | Light/dark themes, compact default layout, optional Advanced controls |
| Distribution | Single-file executables plus Velopack installer/update feed, selectable versions and explicit rollback |

## Start small

For a plain GET, choose an API-only package, enter its URL, configure Auth if needed, and Send request. You do not need a database or fixture. Use **Cloud test** for the included [sign-in and remote-IP sample](../samples/generic/cloud-checkremoteip.md), or `Start-Tester.ps1 -Demo` for the entirely local sample.

Auth shows Swagger first. Manual JWT/API token and registered OIDC client configuration are collapsed. **Advanced** exposes database/transport configuration without changing any saved request settings. Use the × button beside a field to remove it; disabling its checkbox keeps it in the draft but omits it from the request.

## Current limits

Windows is required for the desktop and Windows-protected profiles. Swagger requires Edge WebView2 Runtime. Package size and response/body limits are enforced. Imported bearer tokens do not refresh automatically. Microsoft can require MFA again despite retained browser cookies. Query Studio is for read-only verification, not migrations or setup writes.

OpenAPI import is JSON-only (no YAML or external references). JSON Schema assertions, remote MCP hosting, arbitrary setup/teardown SQL and a managed cloud service are not implemented; preparation is limited to exact, reviewed plans. The cloud sample uses a placeholder URL and is not a dependency of the application.

See the [workflow guide](packages-and-playground.md), [agent authoring](agent-authoring.md) and [changelog](../CHANGELOG.md) for version history.
