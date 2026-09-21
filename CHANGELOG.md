# Changelog

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
