# F05 — Fixtures, database checks and Query Studio

Depends on: provider adapters, evidence contract, secret references and run coordinator.

## Connections and providers

Support Microsoft SQL Server, MySQL, PostgreSQL and SQLite. Each environment maps logical connection names to local settings and credentials. Test connectivity separately from test execution. A workspace may use several connections, including separate logical databases in the same environment.

Keep SQL dialects explicit. Provider-neutral orchestration does not make LIMIT/TOP, JSON functions, identifier quoting or parameter types portable. Saved query variants may share a logical query ID but have provider-specific SQL.

## Fixture workflow

Fixtures can be literal JSON values/rows or fetched with predefined parameterized queries. Define expected cardinality, required columns/types and selection policy. If multiple rows need a human choice, show a table and require selection; never silently pick the first row. An intentional auto-selection query needs deterministic ordering.

Capture connection alias, query/version, parameter snapshot, retrieval time, chosen row and redactions. Freeze selected values for the attempt. Reload is explicit. Missing/stale data or schema mismatch blocks preparation with an explanation rather than becoming an API failure.

## Verification after an API call

Run predefined queries using fixture values and captured response IDs as typed parameters. Compare scalar values, row existence/count, selected columns or before/after snapshots against declared expectations. Attach all checks to their triggering attempt. Optional polling for eventually consistent data has a recorded interval, deadline and observations; do not repeatedly send the API request.

A query failure is INCONCLUSIVE for the unavailable check; a successful query contradicting an expectation is FAIL. Previously definite API/check failures remain failures. Mark capped/truncated results: assertions requiring the full result cannot pass using incomplete evidence.

## Query Studio

Small SQL editor with connection picker, typed parameters, Run/Cancel, duration, bounded result grid, errors, save-as-query and **Attach to this attempt**. Allow promoting a useful query into a fixture or verification definition. Query execution alone is evidence collection, not an automatic pass.

Default to read-only database accounts for fixture/verification work. Text scanning for SELECT is not a security boundary: stored functions, CTEs and provider features can mutate data. Enforce access with DB permissions. Writable setup/cleanup queries are separately labeled capabilities and explicitly enabled per environment/run. Do not add production write access as a default.

If later setup/teardown is enabled, use owned fixture identifiers and matching cleanup, log each operation, and never imply that a local SQL transaction rolls back remote API effects. Failed cleanup stays visible in the report.

## Acceptance

Run provider-specific integration tests against all four engines. Verify null/empty, decimal precision, GUIDs, timestamps, parameter binding, timeouts and cancellation. Fixture selection must be reproducible from evidence. A failed DB check, unavailable DB and truncated results produce distinct outcomes. User cancellation does not claim that a write was rolled back.
