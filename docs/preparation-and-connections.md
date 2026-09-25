# Database connections and preparation

## Easier connections

Open Query Studio and choose **+ Connection**. Choose a database type and enter the server, port, database, username and password supplied by your administrator. Port 0 uses the default. SQLite uses a file picker; SQL Server supports Windows authentication. Server connections use encrypted transport. Advanced driver options remain available through the existing connection-string field.

Administrators can export a connection template for testers to import. Templates contain connection details and usernames, but never passwords. Enter the password locally. Workspace → Save environment remembers connection credentials in Windows user-encrypted storage; otherwise connections remain session-only.

Query Studio remains read-only. Confirm the read-only checkbox only when the account is restricted by the database administrator. Write-capable profiles work for both queries and preparation; leave the optional read-only checkbox off for these accounts.

## Prepare, sign in, send, next

In a case's **Preparation** tab, package authors choose **Configure preparation** to describe the setup. Testers choose **Prepare** beside Send to review the target and value before applying it. Optional flag labels appear as checkboxes and combine through a 64-bit bitwise OR.

Preparation is optional and never executes merely by opening a package. Cases that declare preparation cannot be sent until it succeeds. Packages containing preparation use schema **1.5**, defined in `schemas/test-package.v1.5.schema.json`; older packages remain supported.

### Database value

Use `kind: databaseValue`, `connectionRef`, `provider`, `table`, `keyColumn`, `keyJson`, `valueColumn` and `valueJson`. JSON scalars distinguish numbers, strings, booleans and null. Credentials stay out of packages.

The engine captures exactly one row, saves the original locally before writing, compares against that original and verifies the replacement in a transaction. SQL identifiers are validated and values are parameters. This changes one column in one identified row; it does not execute arbitrary SQL scripts.

### API value

Use `kind: apiValue`, a GET `captureRequest`, a `valuePointer` such as `/permissions`, and a PUT/PATCH/POST `updateRequest`. Its JSON body includes the unquoted placeholder `{{valueJson}}`, for example `{"permissions":{{valueJson}}}`. The same operation restores the original scalar.

The engine sends the captured ETag as If-Match and verifies the result with another GET. APIs without ETags require explicit `allowUnconditionalApiUpdate: true`; in that mode, another actor could change the value between comparison and update. Redirects and automatic retries are disabled. An uncertain change retains its recovery record for reconciliation.

Use a separate administrator identity for API preparation when reducing the tested account's permissions, so restoration remains possible. Database preparation does not require another test identity.

### Refresh the existing identity

Set `requireFreshLogin: true` to clear the old token after preparation while keeping the same identity alias and WebView2 profile. Obtain a new access token through Swagger's authorization flow and import it. Importing the cached old token does not satisfy this requirement.

Optional `jwtClaim` compares the decoded claim with the applied value. This is a testing check, not signature verification. If a suite uses different aliases for the same actual account, select that account in Auth and choose **Use this identity for all test cases** before preparing. Administrator identities in preparation API requests remain separate.

## Restoration and recovery

Preparing another scenario restores the previous preparation first. Finish run, normal app exit, or **Restore changes** restores original values in reverse order. A conflict or restoration failure prevents finishing or normal closing. Values changed by another actor are not silently overwritten.

Pending changes survive a crash in the workspace's Windows user-encrypted `Preparation/profiles.protected` file. Reopen the same workspace, reload the original connection environment, and use Restore changes. Database recovery requires the original connection profile; API recovery requires the original base URL and administrator identity. Restoration does not change an issued token; obtain another JWT when the original permissions are needed.

Preparation reports use schema **1.8** and include preparation/restoration events in JSON, readable text and HTML. Original values remain in encrypted local recovery storage, outside the audit summary. MCP supports explicit prepare_environment, list_preparations and restore_environment tools on Windows with encrypted recovery. Exact preparation plans and writable database aliases must be approved in local host configuration. See [the agent preparation guide](../skills/e2e-tester-author/references/preparation.md).

**Next case** is also beside **Send request** to shorten the repeated mouse movement.

## Connection troubleshooting

Test connection uses a fixed SELECT 1 and does not require the profile to be marked read-only. Query Studio accepts write-capable accounts; preparation requires writable access. MySQL/MariaDB setup exposes TLS mode and preserves existing settings. Connection errors report safe driver codes; query errors also identify the failing stage. See [compatibility guidance](../skills/e2e-tester-author/references/validation-and-compatibility.md). Run `./Test-Databases.ps1 -MariaDb` to verify the MySQL provider against disposable MariaDB infrastructure; omit the switch for MySQL. Neither run certifies multi-node Percona behavior.
