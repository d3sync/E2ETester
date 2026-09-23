# Preparation and restoration

Use schema 1.5 when a case includes `preparation`. Ordinary suites remain compatible with 1.4. The schema and `assets/prepared-permissions.jsee` describe the exact fields. Examples contain placeholders: obtain the real account key, endpoint and expected status before producing an executable suite.

Keep the initial Swagger authentication and access-validation pair. Attach preparation to the later business case. Fixtures remain optional and are separate from preparation. Retain the same `identityRef` for permission changes to the same account: a new alias creates a separate browser profile, not new permissions.

`databaseValue` changes exactly one scalar column selected by a key. Supported providers are sqlServer, mySql, postgreSql and sqlite. Use a locally configured writable connection alias; never include its connection string. `keyJson` and `valueJson` contain JSON scalar text, such as `"123"` for numeric 123. Flags are named nonnegative 64-bit values combined with bitwise OR. Do not invent permission enums, account identifiers or database columns.

`apiValue` captures a scalar using a GET and JSON pointer, then updates it through PUT/PATCH/POST with an unquoted `{{valueJson}}` placeholder in the JSON body. Redirects are disabled. ETag/If-Match protects writes; `allowUnconditionalApiUpdate` must be an explicit choice for an API lacking ETags. When changing the tested account's permissions, use a separate administrative identity for capture and update so restoration remains possible.

Set `requireFreshLogin: true` when changed permissions require a newly issued JWT. Set `jwtClaim` only when the claim name and representation are known. The existing token does not change. Obtain a new token for the same identity after Prepare. Token/claim checks are workflow checks, not signature validation. The API remains responsible for authorization.

## MCP execution

Inspect capabilities first. Preparation requires Windows, `allowPreparation: true`, and an exact matching plan in host `approvedPreparations` (including target key and desired value). Database profiles additionally require `allowPreparation: true`; read-only query access alone does not authorize writes. Host configuration is outside tool control. Do not broaden approvals automatically while generating a package.

1. Validate and start the suite; complete its initial sign-in and access-validation cases.
2. For an authorized prepared case, call `prepare_environment(runId, caseId, operationId)` with a new UUID. Preserve and reuse this UUID after a timeout; persisted IDs never reapply, including after restoration. Inspect `list_preparations` for pending/applied/restored state.
3. Obtain a fresh token for the **same alias** if requested. Use `authenticate_identity` only for a host-configured browser identity. With environment-based bearer tokens, the user must securely update the launching environment and restart MCP with the same workspace; MCP cannot retrieve desktop Swagger tokens or modify its parent's environment. Never ask for tokens in chat.
4. Call `prepare_case`, then `execute_prepared`, and inspect its operation and attempt. `prepare_case` only previews the HTTP execution; it does not mutate the database.
5. Call `restore_environment` before preparing another scenario. Call `finish_run` to restore remaining changes and finish. If a write was interrupted or a row changed independently, inspect state and resolve the conflict; do not retry with a new UUID or bypass restoration.

Original values are encrypted for the current Windows user in the MCP workspace's `Preparation` directory. Keep this workspace across restarts and until restoration completes. Exiting MCP preserves pending recovery; it does not automatically restore. Reports include preparation audit events using report schema 1.8. After restoration, obtain another token before testing restored permissions.
