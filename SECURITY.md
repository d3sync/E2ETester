# Security policy

Security fixes target the latest release. Older releases remain available for rollback but do not receive a guaranteed maintenance window.

## Reporting

Use the repository's **Security → Report a vulnerability** feature when private reporting is enabled. Otherwise, contact a maintainer privately; if a private channel is not listed, ask for one without publishing exploit details. The project has no dedicated public security mailbox yet.

Include the affected version, a synthetic reproduction, expected/actual behavior and impact. Remove credentials, cookies, personal data and real business request/response bodies. Never attach your local browser profile or protected environment files.

## Boundaries

- Packages describe requests, fixtures and assertions. Review the target and method before sending; packages can describe real mutations.
- HTTP attempts are journaled before sending. Transport failures may mean delivery is unknown; requests are not automatically replayed.
- Known credentials and sensitive fields are redacted, but business data may remain in reports and database evidence. Inspect exports before sharing.
- Swagger runs in a persistent WebView2 profile. Cookies and saved passwords stay in that browser profile. Token capture is restricted to the chosen API origin; login forms are not scraped.
- Windows-protected environment profiles are separate from exported packages. They are tied to the Windows account, not a portable secret vault.
- SQL access relies on read-only database credentials as well as query validation. Do not point the query studio at a privileged account.
- MCP deployments must configure allowed targets, credentials, authoring/query permissions and optional interactive login deliberately.
- Update feeds supply executable code. Use a feed you control/trust and HTTPS or a trusted local directory. Package hash verification is not publisher authentication; release signing remains a deployment responsibility.

See [architecture](docs/architecture.md), [MCP setup](docs/mcp.md) and [updates](docs/updates.md).
