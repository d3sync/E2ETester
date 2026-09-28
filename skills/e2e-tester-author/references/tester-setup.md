# Reusable tester setup (0.8.1)

Use package schema `1.8` for signed-in account bindings and case applicability. Earlier schemas remain supported.

A database preparation can resolve its key from an existing named JWT:

```json
{
  "keyColumn": "ID",
  "keyJson": "\"{{identity.cloud-user.userid}}\""
}
```

This is a complete JSON string, not string interpolation inside SQL. Use a unique database column compatible with the claim's value. The alias must not contain dots or braces. The claim must exist, be scalar, and come from an unexpired bearer JWT. Claims are decoded supplied values, not independently verified authentication. The API verifies its own tokens. Do not place GUID claims into numeric key columns.

Sign in to the source identity, configure the named database connection, then open **Tools → Review tester setup**. This read-only review shows resolved targets, reads original database values, and flags assumptions. It makes no permission changes. Prepare still captures and verifies the original at Apply time. Recovery pins the concrete row and original value; changing identities later cannot redirect restoration. Use **Restore changes**, including its entry inside Guided Actions. Never generate a fixed-value restoration action from the author's original permissions.

Use distinct stable role aliases. A valid role token must have the required permissions and the same bound account claim as the source identity to bypass preparation. Reuse it until expiry. Changing database permissions does not change issued JWTs.

Cases can declare assumptions about the source identity:

```json
"appliesWhen": [
  { "identityRef": "cloud-user", "claim": "group", "expectedJson": "\"1\"", "description": "Fixtures belong to group 1" }
]
```

`expectedJson` is serialized JSON; matching is type-sensitive (string `"1"` differs from number `1`). Use claim names and types confirmed from the authentication contract. Missing claims also produce warnings. Mismatched cases remain available: select **Run anyway** and confirm. The attempt records the warning and override; expected status/assertions are unchanged. This does not pick new fixtures or infer tenancy rules. Guided HTTP actions with conditions ask for explicit confirmation too.

MCP/headless resolves bindings with host-configured identities and checks applicability. A mismatch stops execution as inconclusive; there is no implicit override. Exact host approval for a preparation uses its original template, before claim resolution. GUI sessions are not exported to headless execution. Validate final packages using the 1.8 schema and runtime `validate_package` without running them.
