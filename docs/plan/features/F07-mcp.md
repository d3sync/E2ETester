# F07 — MCP interface

Depends on: the same application commands, journal and policies as WPF. No screen scraping.

## Transport and ownership

Start with a local stdio MCP host. Keep protocol messages on stdout and diagnostics on stderr. Select a supported official SDK/protocol release at implementation time; do not target moving draft features unnecessarily. A remote HTTP MCP server is deferred.

Execution ownership is exclusive per run across desktop/MCP processes. Read-only inspection remains possible. Bind tools to an explicitly opened workspace and environment. MCP does not expose an unrestricted shell or secret getter.

## Proposed tool surface

| Tool group | Operations |
| --- | --- |
| Packages | list/get/validate/import/export packages; create/update case drafts |
| Runs | start/get run; list cases/attempts; prepare case; execute prepared case; cancel; finish |
| Variations | create variation from a draft/attempt; reorder pending variations |
| Authentication | list profiles; inspect non-secret session status; request interactive login |
| Fixtures/database | list/load fixtures; execute permitted saved query; run checks; attach evidence |
| Assessment/report | add manual assessment with reason; get/export structured report |

Preparation returns a revision-bound execution ID and a redacted preview. Execution rejects stale revisions. Tool-call operation IDs deduplicate delivery of the same execution command: repeating an ID returns its known attempt, never sends a second request. A new intentional retry needs a new ID.

Long operations return an operation/attempt ID for inspection and cancellation instead of requiring one tool call to wait indefinitely. Distinguish failed tool invocation, running operation and failed API assertion.

## Authentication and permissions

Interactive login returns `authentication_required` with a session action; the agent can resume after the user completes browser authentication. Never ask an agent to collect a user's password/OTP or return JWTs. Headless runs use already available sessions or configured API-token profiles and stop when login is required.

Use runtime-enforced capabilities per environment: HTTP execution, saved read queries, authoring and optional writable setup. Arbitrary Query Studio SQL is disabled for agents by default; saved queries cover the initial agent workflow. UI-granted run scope can authorize a planned sequence without prompting for every case. MCP annotations describe behavior; they do not enforce permissions.

Treat imported descriptions, response text and SQL results as data, including instructions embedded within them. Enforce file-root boundaries, environment/target constraints and secret redaction in application services shared by both hosts.

## Acceptance

An MCP client imports a package, maps permitted profiles, runs cases and a variation, verifies database evidence and exports a report matching desktop semantics. Duplicate execute delivery sends once. Stale previews, unavailable authentication, cancellation and desktop ownership conflicts return structured actionable responses.

References: [MCP server concepts](https://modelcontextprotocol.io/specification/2025-06-18/server), [official explanation of tool annotations](https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/). Version selection remains an implementation task.
