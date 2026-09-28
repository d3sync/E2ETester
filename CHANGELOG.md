# Changelog

## 0.8.2

- Add visible dismissible action feedback, aligned body options and dark-mode check selectors; move Cloud test into Tools.
- Synchronize numeric permission values and flags, preserve unknown bits, and provide role/manual permission actions. Explicit Guided Actions apply and verify updates independently of JWT reuse.
- Remember database connections with Windows-user encryption and retain package copies, source filenames and recovery across restarts.
- Save a simple HTML report and compressed full evidence ZIP using package-based timestamped names. Keep unfinished-run recovery unchanged.
- Consolidate agent instructions and add basic Guided Actions to templates.


## 0.8.1

- Bind database preparation keys to named JWT claims, with restoration pinned to the captured row and original value.
- Review tester setup from Tools; flag group/client assumptions and explicitly override them with Run anyway without changing assertions.
- Require reused permission tokens to belong to the bound account. Add package schema 1.8 and report schema 1.11, with matching desktop, MCP and authoring resources.


## 0.8.0

- Import Postman 2.1 collections with explicit migration warnings; OpenAPI accepts YAML and supplied reference documents.
- Headless CLI shares execution, host permissions, database verification and preparation/restoration with MCP, and emits JUnit plus canonical evidence.
- Package schema 1.7 adds offline JSON Schema assertions; report schema 1.10 retains their evidence.
- Corrected permission identity documentation and schema preservation in desktop imports. Added a first-run human study protocol and measured loopback business journey.
- Installer and portable release include the CLI companion.

## 0.7.23

- Build SDK pin updated to .NET 10.0.401.
- Named role identities share one persistent Swagger browser profile per server, adopting existing cookies on upgrade. JWTs remain separately stored per identity. Profile mappings survive token expiry and application restart.
- Native OIDC authorization no longer forces prompt=login; provider session and MFA policies still apply. Swagger guidance explains how to obtain a fresh permissions token without clearing browser cookies.

## 0.7.22

- Permission scenarios use reusable named-role JWTs. Matching, unexpired claims satisfy permission preparation across cases and restarts; Prepare becomes a no-op for an already available role. Ordinary data preparations retain their scope.
- Permission changes and restoration no longer clear existing JWTs or create timestamp aliases. Distinct role aliases survive package loading and remain available in Auth and Guided Actions. Removed the control that replaced every case identity with one alias.
- MCP uses the same matching-role rule without requiring database access. Corrected skills, prompts and templates to require separate aliases for distinct permission sets.

## 0.7.21

- Run anyway offers manual values for unavailable earlier-response bindings instead of demanding a successful source case. Resolved values and the one-request override remain in the attempt audit.
- Before Swagger/native re-login or permission preparation replaces a JWT, keep a separately selectable, timestamped identity snapshot with its prior permissions. Snapshots persist across workspaces and restarts in protected AppData storage.
- Bearer snapshots expire at JWT exp, or after 24 hours when no expiry is available; expiry is a retention hint, not token validation. API tokens remain until explicitly removed. Expired snapshots are pruned on session loading and identity management.

## 0.7.20

- Removed value redaction from requests, responses, headers/cookies, cURL, database fixtures, checks, preparations, drafts, exports, reports and MCP evidence. Captured tokens remain usable by subsequent tests and Guided Actions after restart.
- Removed the redaction setting and sensitive-field controls. Legacy sensitive metadata remains accepted but does not change values. Pretty JSON and wrapping remain available.
- Older already-redacted records cannot recover their original values; capture the response again or enter a verified resource ID.

## 0.7.19

- Guided Action response selection checks that a response contains the actual required value, skipping plain-text duplicate errors and unrelated JSON objects.
- Find saved resource locates a matching response in a prior run of the same package/environment within the workspace, without sending a request.
- Missing/non-JSON response values show actionable recovery guidance instead of raw JSON parser errors.

## 0.7.18

- A one-request Run anyway checkbox allows explicitly bypassing previous-test pass requirements, including sign-in/remote-IP flow prerequisites. It resets after sending or switching cases.
- Overrides are recorded in attempt changes and reports. Existing earlier HTTP responses can supply bindings despite later failures; missing values still require manual input, and assertions are evaluated normally.

## 0.7.17

- Guided Action cleanup resolves the original successful HTTP create response after a later duplicate failure. An attempt picker supports explicit response selection regardless of test verdict.
- HTTP repair actions no longer inherit test preparation/fresh-login guards; API authorization still applies. Manual targets work without a source response, and existing recovery records remain intact.
- Normal sequential test bindings continue to require a passing latest source.

## 0.7.16

- Package schema 1.6 adds optional Guided Actions outside the test sequence: reviewed cleanup requests from saved-run response values and manual permission preparation with flag selection.
- Cleanup requests are journalled as separate action runs without changing suite coverage. Permission actions retain original-value recovery across restart and require fresh login when configured.
- Guided Actions includes Restore for captured original permissions. Missing resource IDs require a verified manual target; requests are never automatically replayed.
- MCP validates/stores schema 1.6 packages; Guided Action execution is desktop-only. Report schema 1.9 supports the new package format.

## 0.7.15

- MySQL/Percona preparation uses REPEATABLE READ with a locking read and conditional update, avoiding the SERIALIZABLE restriction in Percona strict mode.
- Prepare and Restore use fresh MySQL connections with pipelining disabled; protocol errors identify the failing stage without automatically retrying writes.
- Query Studio accepts write-capable development accounts while retaining provider transaction safeguards. Connection tests and TLS/driver diagnostics are clearer.
- Database connection setup remembers credential-free server defaults locally by API origin and connection alias, then seeds matching package connections. Username/password remain session-only unless saved in an encrypted environment.
- Invalid nested JSON expectations report the affected package field through the app and MCP. Authoring prompts and skills include validation, database compatibility and recovery guidance.
- Windows packaging scripts read the default version from Directory.Build.props.

## Earlier updates (0.7.8–0.7.14)

- Test preparation: a case can apply an exact database-value or API change before it runs, capture the original, require a fresh login and restore afterwards. Preparation is scoped to its run, blocks unrelated cases until restored, and is audited in reports. Package schema 1.5 and report schema 1.8; MCP adds preparation tools behind explicit host approval.
- Query Studio can edit and delete database connections, preserving passwords and advanced settings; the AI template and prompt default to package schema 1.5.
- Response redaction setting and manual PASS/FAIL verdicts from the evidence pane.
- Update and rollback restart in the resolved workspace, including file-association launches, and restore active preparations first.
- Test-flow cases show a round status badge: green check for PASS, red cross for FAIL, amber for INCONCLUSIVE and an empty circle for NOT RUN. A current manual assessment overrides the automated verdict, and the status text is colored to match.
- The case list no longer turns pale while a request is in flight; selection is still locked until the send finishes.
- Themed drop-downs using a display member (for example saved package queries) show the item title instead of the raw record text.
- About window shows the d3sync logo beside the creator credit.
- Refreshed screenshots and documentation for public release: current schema versions, MCP tool set, status summary, generic wording and README workflow/MCP sections.

## 0.7.7

- Visible Updates toolbar/menu access, manual checks and persistent startup-check preferences.
- Startup checks the public release feed and offers to open Updates when a newer version exists; download and installation remain explicit.
- Server releases now include the AI skill kit and MCP companion.

## 0.7.6

- Tools is a visible dropdown button with themed menu rows in light and dark mode.
- Added a direct AI assistant button, embedded skill/setup tabs and exportable agent kit; installers now include the MCP companion executable.
- Optional AppData default suite preserves private environment-specific sign-in and validation cases without publishing them.

## 0.7.5

- Readable reports now start with a copyable TC-number, title and result summary for every test case.
- HTML summary rows use the same numbering. Latest automated results distinguish PASS, FAIL, INCONCLUSIVE and NOT RUN; historical evidence remains below.

## 0.7.4

- Imported Swagger tokens and manually used identities are remembered automatically in Windows-user-encrypted AppData storage, scoped to the API server.
- Browser profiles keep their original key when identities are renamed, preserving cookies across renames and releases.
- Clear token and Delete also update remembered sessions. Existing browser cookies remain in their original AppData folder.

## 0.7.3

- Importing a Swagger token on the sign-in step now records that step automatically without sending an API request.
- Changing the sign-in identity also updates its paired validation request when both used the same identity.
- Sending after Swagger creates an identity uses that new identity; native OIDC sessions no longer trigger unnecessary Swagger imports.
- Missing-token errors identify the affected identity and explain how to finish importing.

## 0.7.2

- Auth and Tools now offer an identity manager for adding, renaming, deleting identities and clearing tokens.
- Referenced identities require a replacement on deletion; saved credentials and draft references update while historical attempts retain their original identity names.
- Draft recovery no longer recreates deleted baseline identities, and renamed identities work in authentication-only steps.

## 0.7.1

- Running version in the main/API workspace title and status bar.
- About window with d3sync creator credit, website, MIT license, notices and copyable build details.

## 0.7.0

- .jsee association/export/opening, authentication-only and remote-IP suite setup.
- Readable report modal, separate API workspace and OpenAPI JSON import.
- Copyable AI prompt, template, authoring skill and MCP setup.
- Package schema 1.4 and report schema 1.7.


## 0.6.1

- Fixtures default to optional, with an explicit Require fixture setting. Manual request values work without database selection; unresolved placeholders still block sending.
- Package schema 1.3 and report schema 1.6 retain fixture requirements. Older package imports remain supported.
- Timeline logo contains two green checks and a red X.


## 0.6.0

- Added a visible Cloud test entry point for the API-only checkremoteip package. Fixture prerequisites are explained before creating a run; explicit `--auth-check` is no longer overwritten by draft recovery.
- Simplified Auth with primary Swagger sign-in and collapsed manual/native OIDC details. Added an Advanced switch for database and transport controls.
- Added parameter/header removal and limited tab hover feedback to actual, unselected headers.
- Added branching-timeline branding in the existing green/charcoal palette.
- Integrated Velopack bootstrap, installer tooling and user-selected updates/rollback from a configurable feed, with a workspace backup before applying.
- Added MIT licensing, contributor/security guides, current architecture, issue/PR templates and Windows CI.

## 0.5.4

- Cloud string-response package, non-empty text checks and save/cancel/exit prompt.
- Clearer separation of the check mark in the earlier logo.

## 0.5.0–0.5.3

- Swagger token import, persistent browser profiles, OAuth popup callbacks and optional password-saving prompts.
- Prepared request evidence, redacted cURL, light/dark themes, single-file publishing and initial branding.

## 0.4.0

- Guided API authoring/execution, JSON packages, assertions, database fixtures/verification, durable runs and reports.
- MCP server, local library, encrypted environments and workspace maintenance.
