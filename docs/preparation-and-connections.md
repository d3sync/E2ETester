# Database connections and preparation

## Easier connections

Open Query Studio and choose **+ Connection**. Choose a database type and enter the server, port, database, username and password supplied by your administrator. Port 0 uses the default. SQLite uses a file picker; SQL Server supports Windows authentication. Server connections use encrypted transport. Advanced driver options remain available through the existing connection-string field.

Administrators can export a connection template for testers to import. Templates contain connection details and usernames, but never passwords. Enter the password locally. Workspace → Save environment remembers connection credentials in Windows user-encrypted storage; otherwise connections remain session-only.

Query Studio remains read-only. Confirm the read-only checkbox only when the account is restricted by the database administrator. Write-capable profiles work for both queries and preparation; leave the optional read-only checkbox off for these accounts.

## Prepare, sign in, send, next

In a case's **Preparation** tab, package authors choose **Configure preparation** to describe the setup. Testers choose **Prepare** beside Send to review the target and value before applying it. Optional flag labels appear as checkboxes and combine through a 64-bit bitwise OR.

Preparation is optional and never executes merely by opening a package. Cases with data preparation need successful preparation; permission cases may reuse a matching valid identity. Packages containing preparation use schema **1.5**, defined in `schemas/test-package.v1.5.schema.json`; older packages remain supported.

### Database value

Use `kind: databaseValue`, `connectionRef`, `provider`, `table`, `keyColumn`, `keyJson`, `valueColumn` and `valueJson`. JSON scalars distinguish numbers, strings, booleans and null. Credentials stay out of packages.

The engine captures exactly one row, saves the original locally before writing, compares against that original and verifies the replacement in a transaction. SQL identifiers are validated and values are parameters. This changes one column in one identified row; it does not execute arbitrary SQL scripts.

### API value

Use `kind: apiValue`, a GET `captureRequest`, a `valuePointer` such as `/permissions`, and a PUT/PATCH/POST `updateRequest`. Its JSON body includes the unquoted placeholder `{{valueJson}}`, for example `{"permissions":{{valueJson}}}`. The same operation restores the original scalar.

The engine sends the captured ETag as If-Match and verifies the result with another GET. APIs without ETags require explicit `allowUnconditionalApiUpdate: true`; in that mode, another actor could change the value between comparison and update. Redirects and automatic retries are disabled. An uncertain change retains its recovery record for reconciliation.

Use a separate administrator identity for API preparation when reducing the tested account's permissions, so restoration remains possible. Database preparation does not require another test identity.

### Reuse named permission identities

Use a distinct stable identity alias for each permission set, such as `cloud-user` and `cloud-reader`. With `requireFreshLogin: true` and `jwtClaim`, a matching unexpired JWT satisfies permission preparation. Reuse it across cases and sessions without another database update or login. Prepare and authorize only when that role token is missing, expired or has the wrong claim. Restoring database permissions does not invalidate issued JWTs.

Role identities share persistent Swagger browser sign-in per server. After a permission change, choose Swagger Logout then Authorize to obtain a new token; import it into the selected role identity. Keep the browser cookies. Provider session/MFA policies still apply. Non-permission data preparation retains its operation scope.

## Restoration and recovery

Preparing another scenario restores the previous preparation first. Finish run, normal app exit, or **Restore changes** restores original values in reverse order. A conflict or restoration failure prevents finishing or normal closing. Values changed by another actor are not silently overwritten.

Pending changes survive a crash in the workspace's Windows user-encrypted `Preparation/profiles.protected` file. Reopen the same workspace, reload the original connection environment, and use Restore changes. Database recovery requires the original connection profile; API recovery requires the original base URL and administrator identity. Restoration does not change an issued token; obtain another JWT when the original permissions are needed.

Preparation reports use schema **1.8** and include preparation/restoration events in JSON, readable text and HTML. Original values remain in encrypted local recovery storage, outside the audit summary. MCP supports explicit prepare_environment, list_preparations and restore_environment tools on Windows with encrypted recovery. Exact preparation plans and writable database aliases must be approved in local host configuration. See [the agent preparation guide](../skills/e2e-tester-author/references/preparation.md).

**Previous**, **Send request** and **Next case** share one execution bar under the request, with the readiness line above it; View report, Export report and Finish run stay in the footer.

## Connection troubleshooting

Test connection uses a fixed SELECT 1 and does not require the profile to be marked read-only. Query Studio accepts write-capable accounts; preparation requires writable access. MySQL/MariaDB setup exposes TLS mode and preserves existing settings. Connection errors report safe driver codes; query errors also identify the failing stage. See [compatibility guidance](../skills/e2e-tester-author/references/validation-and-compatibility.md). Run `./Test-Databases.ps1 -MariaDb` to verify the MySQL provider against disposable MariaDB infrastructure; omit the switch for MySQL. Neither run certifies multi-node Percona behavior.

MySQL preparation and restoration use fresh connections with protocol pipelining disabled, retaining the configured TLS settings. Protocol errors identify the failing stage; no write is automatically retried. If recovery is pending, use Restore changes before preparing again. Database proxies and server versions may still need investigation if a protocol error persists.

## Remembered connection defaults

Adding or editing a connection through the setup dialog remembers its noncredential fields in `%LOCALAPPDATA%/E2ETester/Connections`, scoped by API origin and connection alias. Opening a package seeds missing connections referenced by its saved queries or database preparations. In Query Studio, select the seeded connection and choose Edit to supply username/password. Existing connections in the session are never overwritten. Deleting a connection also removes its defaults.

These files use a list of the existing connection-template objects; username is cleared and the format has no password field. They remain local, outside the `.jsee` package. Credentials are session-only unless explicitly saved in an encrypted workspace environment. Connection strings edited directly in Advanced settings are not automatically captured as defaults; use the setup dialog to save defaults.

Transaction isolation is an application implementation detail, not a package setting. An older executable that starts SERIALIZABLE transactions cannot be repaired by relabeling a package schema or editing a preparation description. Use a build containing the MySQL REPEATABLE READ fix.
