# Feature overview

| Area | Available |
| --- | --- |
| Guided tests | Sequential cases with PASS/FAIL/INCONCLUSIVE status badges, custom variations, editable defaults, required/optional fields, parameter/header deletion |
| HTTP | Standard/custom methods, text/JSON/form/multipart/binary bodies, cookies, bounded responses, same-origin redirect support |
| Authentication | Swagger/WebView2 token import, manual JWT/API tokens, registered native OIDC/PKCE, multiple identities |
| Assertions | Status, headers, non-empty/substring text, JSON values/presence/comparisons/array counts, duration, JSON Schema (package 1.7) |
| Database | SQL Server, MySQL, PostgreSQL, SQLite; SELECT queries (read-only enforced for SQLite; server providers rely on account permissions), typed parameters, fixtures and post-request verification |
| Evidence | Durable attempts, complete captured values, prepared request/cURL, response/history, manual assessments, JSON/HTML/ZIP reports |
| Workspace | Recovery drafts, save-on-exit choices, library, encrypted environments, backups and retention controls |
| Preparation | Optional, host-approved setup (database value or API change) before a case, with captured originals and verified restoration |
| API workspace | Ad-hoc requests, Swagger 2.0 / OpenAPI 3.x JSON/YAML and Postman 2.1 import, cURL evidence |
| Automation | Headless CLI with JUnit and canonical reports; MCP stdio tools with explicit target/query/authoring/preparation permissions and idempotency; AI prompt, template and skill kit |
| Appearance | Light/dark themes, compact default layout, readiness line above Send, advanced database/transport tabs on demand |
| Distribution | Single-file executables plus Velopack installer/update feed, selectable versions and explicit rollback |

## Start small

For a plain GET, choose an API-only package, enter its URL, configure Auth if needed, and Send request. You do not need a database or fixture. Use **Tools → Cloud test** for the included [sign-in and remote-IP sample](../samples/generic/cloud-checkremoteip.md), or `Start-Tester.ps1 -Demo` for the entirely local sample.

Auth shows the identity state with one primary action (Swagger sign-in, browser sign-in, or sign in again). The Swagger address, manual JWT/API token and registered OIDC client are under **Other sign-in methods and settings**. The readiness line above **Send request** states what the next request still needs and offers that action. The Database and Transport tabs appear automatically for packages with queries or checks, or through **Tools → Show advanced tabs**, without changing any saved request settings. Use the × button beside a field to remove it; disabling its checkbox keeps it in the draft but omits it from the request.

Keyboard: Alt+S sends (or completes the sign-in step), Alt+D / Alt+A move to the next / previous case, Alt+W prepares, Alt+B opens Swagger sign-in, Alt+Q Query Studio, Alt+G Guided Actions, Alt+R View report, Alt+X cancels, Ctrl+S saves the draft, Ctrl+O opens a package and F1 opens **Tools → Keyboard shortcuts…**, where every binding can be changed. Notifications appear as a dismissible toast in the lower right and fade after ten seconds; the footer keeps the latest message.

## Current limits

Windows is required for the desktop and Windows-protected profiles. Swagger requires Edge WebView2 Runtime. Package size and response/body limits are enforced. Imported bearer tokens do not refresh automatically. Microsoft can require MFA again despite retained browser cookies. Query Studio is intended for SELECT verification, not migrations or setup writes; use preparation for setup changes.

OpenAPI JSON/YAML accepts local references and explicitly supplied reference documents; remote references are never fetched automatically. Postman scripts and unsupported authentication/body modes need manual conversion, with import warnings. Remote MCP hosting, arbitrary setup/teardown SQL and a managed cloud service are not implemented; preparation is limited to exact, reviewed plans. The CLI uses environment-supplied identities, not interactive login or desktop browser tokens. The cloud sample uses a placeholder URL and is not a dependency of the application.

See the [workflow guide](packages-and-playground.md), [agent authoring](agent-authoring.md) and [changelog](../CHANGELOG.md) for version history.
