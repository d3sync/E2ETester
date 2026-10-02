# Database fixtures and verification

## Optional fixtures

Fixtures are optional by default, even when a saved query is assigned. Under **Advanced > Database**, enable **Require fixture before sending** only for cases that need a selected row. Package schema 1.3 and later store `fixtureRequired` (default `false`); the setting survives recovery and applies to desktop and MCP.

Without a fixture, use manual request values. Replace active `{{fixture.column}}` placeholders or disable those fields before sending. Selected fixtures still resolve values and are recorded. Post-request checks have their own Required setting: required checks remain INCONCLUSIVE until verified; optional checks do not block an otherwise passing API result.

## What is available

Query Studio runs saved or exploratory SQL, displays typed result rows, selects a row as a fixture, and attaches evidence to an API attempt. Cases can declare required or optional row-count/scalar checks. Queries travel in the package; connection strings stay in memory and are never exported.

Providers: SQLite, Microsoft SQL Server, MySQL and PostgreSQL. The implementation uses Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, MySqlConnector and Npgsql. SQLite runs in the standard integration suite. `Test-Databases.ps1` runs the same checks against disposable SQL Server, MySQL and PostgreSQL containers (see [database tests](../infra/database-tests/README.md)). No shared or production database is used by the tests.

## Connect

Open Query Studio and add a connection. Its alias and provider must exactly match the query's `connectionRef` and `provider`. Enter a provider-specific connection string in the masked input; use **Choose SQLite file** for an existing local file. **Test connection** executes a simple read query. Connections must be configured again after restarting the application.

SQLite is opened read-only. A native authorizer also rejects changes, database attachment and attempts to disable query-only mode. SQL Server/MySQL/PostgreSQL accept write-capable accounts. A restricted read-only account is recommended; the optional profile checkbox records that restriction and prevents preparation using that profile. SQL Server/MySQL/MariaDB/PostgreSQL queries run directly without BEGIN, SET TRANSACTION READ ONLY or an explicit rollback. Account permissions determine allowed operations; no read-only account acknowledgement is required. Use Query Studio for SELECT queries and preparation for tracked setup/restore changes. SQLite retains its local read-only protections without an explicit transaction.

## Define queries and checks

Create a query, set its connection alias/provider, enter SQL using `@name` parameters, and add parameter rows with explicit types. Save it in the package, then export the package to preserve it. Queries become frozen when a run starts; exploration remains available, but package changes require a new run.

In a case's **Database** tab, select its fixture query and/or add database checks:

- **RowCount** compares the complete result row count with a nonnegative JSON integer.
- **ScalarEquals** requires exactly one result row and compares a named column with a typed JSON value. Use `"name"` for a string, `42` for a number, `true` for a boolean, or `null` for SQL NULL.

SQL dialects and returned types are provider-specific. A database integer `1` is not silently converted to boolean `true`. Use unique column aliases. Duplicate column names are rejected.

## Fixture selection

Run the case's saved fixture query, select a row, and choose **Use selected fixture**. Selection is never automatic. Failed or truncated query results cannot become fixtures. Selecting a fixture resets request values to the resolved case baseline; subsequent edits show the original resolved values.

Use `{{fixture.column}}` in request fields. A URL binding is percent-encoded. Query/header bindings become field text. In a JSON body, place a whole binding inside a JSON string:

```json
{"id":"{{fixture.id}}","name":"{{fixture.name}}"}
```

If `id` is numeric in the database, it becomes a JSON number, not a quoted string. SQL NULL becomes JSON null. JSON-body bindings must occupy an entire string value; arbitrary expression evaluation and non-JSON body interpolation are not supported. Unchanged bindings survive package export; deliberate overrides become literals.

## Verification and evidence

Send the API request, then click **Verify database** on the selected attempt. Required checks keep the attempt INCONCLUSIVE until verified. A definite failed HTTP check stays FAIL even when the database cannot be checked.

Saved query parameters may reference `{{fixture.column}}` or `{{response./path/to/value}}`. Response paths use JSON Pointer; array indices and `~0`/`~1` escapes are supported. Bindings never replace SQL text: resolved values are assigned to driver parameters. Missing, incomplete or complete evidence cannot silently supply a parameter.

Each verification is recorded before execution. Rechecks retain previous results; the latest result for each check determines the current verdict, and the report's `hadFailures` flag includes historical database failures. An interrupted verification is recovered as INCONCLUSIVE without replay. Finishing a run prevents further evidence changes.

**Attach result to selected API attempt** stores exploratory evidence. It does not satisfy any required check or turn the attempt into PASS.

## Capture limits

Defaults: 30-second query timeout, 1,000 rows. Row limits can be 1–10,000 and timeouts 1–600 seconds. Captured cells are limited to 16 Ki characters/bytes and captured result content to roughly 2 million characters. Additional result sets are marked incomplete rather than silently ignored. A truncated result cannot pass database checks or be selected as a fixture.

Query parameters and result cells retain their actual values, including columns named token or password. Legacy sensitive metadata is accepted but no longer masks values or blocks fixture bindings and scalar comparisons. Driver error text is withheld because it can contain connection details or data; a stable error category is recorded instead.

SQLite uses a native interrupt to bound long-running queries despite the provider's synchronous implementation of async methods. Cancellation and timeout stop waiting/execution where supported and never trigger an API replay.

## Local demonstration

All four adapters have automated integration coverage. Run `./Test-Databases.ps1` from the repository root to add disposable SQL Server, MySQL and PostgreSQL servers to the ordinary HTTP/SQLite suite. The launcher creates restricted database accounts, uses randomly allocated localhost ports and removes its own containers and volumes afterward. See [test infrastructure](../infra/database-tests/README.md).

The **Database demo** button creates `demo-fixtures.db` inside the application's workspace. It only creates its own demo table and inserts missing demo rows. The package is also available at [database-demo.json](../samples/generic/database-demo.json).

This demonstration compares an echoed API ID/name with pre-existing database data. It does not claim to demonstrate server-side database mutation. The integration suite separately changes a test-owned SQLite value and verifies that the subsequent check fails while preserving prior evidence.
