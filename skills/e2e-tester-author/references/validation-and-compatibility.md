# Validation and database compatibility

Use this reference when generating packages or investigating import, connection, preparation or restoration failures.

## Validate the delivered file

Generate new packages as schema 1.5; preparation is optional. Schema 1.4 remains a compatibility format, not the default for new output. Do not change a schemaVersion label to work around a failed validation.

The outer JSON can be valid while nested expectations are invalid. Use a JSON serializer twice: serialize the expected JSON value, then serialize the package containing that string. Examples of package fields:

| Check/value | Field in package JSON |
|---|---|
| jsonEquals string | `"expected": "\"hello\""` |
| jsonEquals boolean | `"expected": "true"` |
| jsonEquals number | `"expected": "42"` |
| jsonEquals null | `"expected": "null"` |
| jsonArrayCount | `"expected": "2"` |
| statusIn | `"expected": "[200,201]"` |
| bodyContains text | `"expected": "hello"` |

UUIDs and numeric-looking identifiers must match the response's real type. Do not silently convert an intended string into a number or boolean. Database expectedJson and preparation keyJson/valueJson also contain serialized JSON values.

Read the exact final .jsee file back from disk and submit those contents to validate_package after every repair or final edit. Require ok: true. JSON Schema checks alone cannot validate JSON nested inside strings. MCP tool availability, save_package on an earlier draft, or calling unrelated tools does not prove the delivered file passed validation. If runtime validation is unavailable, disclose that limit and do not claim the suite is validated by E2E Tester.

An invalid_package_field error provides a JSON path and repair guidance. Fix that field without weakening assertions or guessing business expectations. Older clients may show a generic rejection; do not interpret it as successful validation. Preserve the original file when repairing a supplied suite unless asked to overwrite it.

## Identify the database failure before changing settings

Record provider, server product/version, operation (Test connection, query, Prepare, Restore), and safe error code/stage. MySQL is the connection provider for MySQL, MariaDB and Percona XtraDB Cluster, but their server configuration and supported operations can differ. A MariaDB test does not verify a multi-node Percona deployment.

Test connection uses only SELECT 1 and supports both read-only and write-enabled profiles. A successful probe does not verify query permissions or preparation rights. Query Studio accepts write-capable accounts; a restricted read-only account is recommended, not required. Leave the optional read-only flag off for a write-capable account, which can also be used for preparation. Since 0.8.4, queries execute directly without explicit transactions or SET TRANSACTION READ ONLY. SQLite remains opened read-only; server providers rely on database account permissions. Do not add transaction setup to generated SELECT queries to work around a proxy or cluster. Preparation retains its separate capture/update/restore transactions. Use SELECT queries in Query Studio and the preparation engine for planned changes.

New MySQL profiles default to required TLS. Match the administrator's TLS policy and server support. Preferred can fall back to an unencrypted connection; None disables encryption. Do not automatically weaken TLS, trust certificates, enable public-key retrieval, change server settings, or broaden permissions to suppress an error. Preserve existing advanced options when editing a connection. Templates contain configuration fields, never passwords or live connection strings.

## Preparation compatibility and recovery

Percona XtraDB Cluster with pxc_strict_mode=ENFORCING rejects SERIALIZABLE transactions. The preparation engine's MySQL path uses REPEATABLE READ, an in-transaction SELECT ... FOR UPDATE and a conditional UPDATE that compares the original value. Use this engine instead of generated ad-hoc transaction SQL; keep strict mode enabled. SQL Server, PostgreSQL and SQLite retain their provider-specific implementation.

Use a real unique/primary key and a transactional table for this single-row workflow. The row lock is not a promise of cluster-wide serializable behavior. Deadlocks, certification conflicts, timeouts or lost connections may still occur. Do not automatically replay mutations or replace an uncertain MCP operation UUID with a new one. Inspect persisted preparation status and restore through the original workspace and connection. Do not delete recovery records or overwrite a conflicting external change.

Capture actual execution-time values in report evidence, not just the package baseline. Use stable aliases per permission set and reuse matching unexpired JWTs across cases. Prepare/login only for missing roles; restore database changes before finishing without clearing saved JWTs. Original values stay in encrypted recovery storage.

A MySQL "Packet received out-of-order" error is a protocol failure, not proof of invalid SQL or permissions. Prepare/Restore use fresh non-pooled MySQL connections with pipelining disabled for server/proxy compatibility. Do not disable TLS or replay the write to diagnose it. Record the reported stage, server/driver versions and whether a proxy is involved (unknown is acceptable). If it persists, inspect server/proxy logs; use the existing recovery workflow before another preparation. This mitigation does not prove a particular proxy caused the error.

Reference: https://docs.percona.com/percona-xtradb-cluster/8.0/strict-mode.html#explicit-table-locking

Connection defaults: desktop connection setup remembers noncredential fields by API origin/alias under the user AppData Connections directory. Packages seed matching local defaults; supply username/password in Edit, or explicitly restore an encrypted saved environment. Do not invent package connection-string or isolation-level fields. A package edit cannot fix an old executable choosing SERIALIZABLE; update the executable. Keep environment-specific templates local or in the authorized private repository.
