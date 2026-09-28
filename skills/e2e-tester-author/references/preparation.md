# Preparation and restoration

Use schema 1.6 or newer for new packages with basic Guided Actions; use 1.8 for tester claim bindings. Include `preparation` only when needed. Existing ordinary suites using 1.4 remain supported. The schema and `assets/prepared-permissions.jsee` describe the exact fields. Examples contain placeholders: obtain the real account key, endpoint and expected status before producing an executable suite.

Keep the initial Swagger authentication and access-validation pair. Attach preparation to the later business case. Fixtures remain optional and are separate from preparation. Use stable named identities for distinct permission sets: `cloud-user` for normal/full-access tests, `cloud-clientadmin` for ClientAdmin, and separate descriptive aliases for other exact flag combinations (for example `cloud-clientadmin-telephony`). Reuse the same alias for every case with that permission set, even when all roles belong to one database account. Never flatten role aliases to cloud-user or generate timestamp identities. A valid JWT whose configured claim equals the required value satisfies permission preparation across cases, suites and restarts. Prepare and sign in only when that role token is unavailable, expired or mismatched. Restoring the database does not invalidate already-issued JWTs. Ordinary non-permission data preparation remains scoped to its operation.

`databaseValue` changes exactly one scalar column selected by a key. Supported providers are sqlServer, mySql, postgreSql and sqlite. Use a locally configured writable connection alias; never include its connection string. `keyJson` and `valueJson` contain JSON scalar text, such as `"123"` for numeric 123. Flags are named nonnegative 64-bit values combined with bitwise OR. Do not invent permission enums, account identifiers or database columns.

`apiValue` captures a scalar using a GET and JSON pointer, then updates it through PUT/PATCH/POST with an unquoted `{{valueJson}}` placeholder in the JSON body. Redirects are disabled. ETag/If-Match protects writes; `allowUnconditionalApiUpdate` must be an explicit choice for an API lacking ETags. When changing the tested account's permissions, use a separate administrative identity for capture and update so restoration remains possible.

Set `requireFreshLogin: true` when changed permissions require a newly issued JWT. Set `jwtClaim` only when the claim name and representation are known. The existing token does not change. Reuse a matching valid JWT for the named role; obtain one only when needed after Prepare. Token/claim checks are workflow checks, not signature validation. The API remains responsible for authorization.

## MCP execution

Inspect capabilities first. Preparation requires Windows, `allowPreparation: true`, and an exact matching plan in host `approvedPreparations` (including target key and desired value). Database profiles additionally require `allowPreparation: true`; read-only query access alone does not authorize writes. Host configuration is outside tool control. Do not broaden approvals automatically while generating a package.

1. Validate and start the suite; complete its initial sign-in and access-validation cases.
2. First reuse an available, unexpired JWT whose claim matches the role; skip prepare_environment when it matches. Otherwise, for an authorized prepared case, call `prepare_environment(runId, caseId, operationId)` with a new UUID. Preserve and reuse this UUID after a timeout; persisted IDs never reapply, including after restoration. Inspect `list_preparations` for pending/applied/restored state.
3. Obtain a fresh token for the **same alias** if requested. Use `authenticate_identity` only for a host-configured browser identity. With environment-based bearer tokens, the user must securely update the launching environment and restart MCP with the same workspace; MCP cannot retrieve desktop Swagger tokens or modify its parent's environment. Never ask for tokens in chat.
4. Call `prepare_case`, then `execute_prepared`, and inspect its operation and attempt. `prepare_case` only previews the HTTP execution; it does not mutate the database.
5. Call `restore_environment` before preparing another scenario. Call `finish_run` to restore remaining changes and finish. If a write was interrupted or a row changed independently, inspect state and resolve the conflict; do not retry with a new UUID or bypass restoration.

Original values are encrypted for the current Windows user in the MCP workspace's `Preparation` directory. Keep this workspace across restarts and until restoration completes. Exiting MCP preserves pending recovery; it does not automatically restore. Reports include preparation audit events using report schema 1.8. After restoration, obtain another token before testing restored permissions.

For database variant restrictions, TLS, account roles and conflict handling, read [validation and compatibility](validation-and-compatibility.md). MySQL preparation uses REPEATABLE READ with a locking read and conditional update for compatibility with Percona enforcing mode; do not disable strict mode as a workaround.


## Shared browser sign-in (0.7.23)

Named role identities share a persistent Swagger browser profile per server, while each keeps its own JWT. After changing permissions, use Swagger Logout then Authorize to obtain a newly issued token without clearing browser cookies. Import it into the selected role alias. Existing role tokens stay available. Do not require a new browser profile or repeated MFA for each role; the provider may still require MFA under its policy. Native OIDC no longer forces prompt=login and uses the system browser session, separately from WebView2.
