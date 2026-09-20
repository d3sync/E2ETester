# F08 — Local workspace robustness

Depends on: persistence, environment and secret-store contracts.

## Environments and local secrets

Environment profiles contain service URLs, database aliases, identity mappings and limits. Secrets use Windows user-protected storage and are referenced by opaque IDs. Separate workspace definitions from machine-local credentials. Secret-bearing URL components, headers, cookies, configured JSON fields and query parameters are redacted before persistence or export.

Mask credentials by default in the UI; explicit reveal is temporary. Copy/export uses redacted content by default. Ordinary sensitive fixture data may need per-field policies too; a password filter alone is insufficient. Deleted secrets invalidate profiles cleanly.

## Recovery

Use transactional journal writes and versioned migrations. Persist attempt start before Send and response/evidence as soon as available. On restart, an unfinished network attempt becomes Interrupted/INCONCLUSIVE with delivery unknown; the server may have completed it. Offer database verification or an explicit new attempt, never automatic replay.

Persist case drafts and flow position. Reacquire execution ownership safely when the prior process is gone. If the journal cannot be written before Send, do not send an unrecordable request. If saving fails after Send, show an explicit unpersisted-evidence error and preserve recoverable in-memory data where possible.

## Limits and responsiveness

Apply configurable timeouts, response/body size caps, query row limits and cancellation. Proposed initial defaults: 30-second HTTP/query timeout, 10 MB inline response capture, 1,000 displayed query rows; allow case/environment overrides and record effective limits. Stream larger artifacts, virtualize tables and never block the UI dispatcher.

Report truncation honestly. Assertions that require omitted data remain inconclusive. No silent automatic deletion of history initially: show disk usage and let users clean/export runs explicitly.

## Distribution and maintenance

Propose self-contained Windows x64 packaging, with desktop and MCP executables sharing versioned components. Verify a clean-machine install, writable user-data paths, upgrades, migration rollback strategy and uninstall behavior. Keep user runs separate from installation files. Decide installer and signing mechanism after deployment needs are confirmed.

Diagnostics contain operation IDs and safe error categories, not connection strings or request secrets. Backup/export must clearly distinguish portable test definitions from sensitive local run evidence.

## Acceptance

Crash recovery, disk-full handling, upgrade migration, large response cancellation, missing secret, invalid environment and concurrent-host ownership are exercised. Logs/journal/reports pass a seeded-secret scan. The UI remains responsive while HTTP, authentication and database operations are pending.
