# F04 — Authentication and identity switching

Depends on: local secrets, profile contract, HTTP injection boundary. See [verified findings](../01-integration-findings.md).

## Profile kinds

| Kind | Behavior |
| --- | --- |
| None | Explicit unauthenticated request |
| OIDC user session | System-browser authorization code + PKCE; use access token as bearer |
| Supplied bearer token | User pastes a JWT/access token; no refresh unless an actual refresh-capable session exists |
| Generic API token | Configured header name and exact format, with secret reference |
| Raw identity-provider API token | Exact raw `Authorization` value, for APIs that expect it unprefixed |
| Gateway API token | Preset added only after the gateway's token contract is verified |
| Direct-service identity headers | Advanced mode for calling a service behind its gateway, explicitly distinguished from gateway authentication |

Generic query-token injection may be supported when an API requires it, with URL redaction. Do not guess that an API token is an `X-Api-Key` or bearer token.

## Browser flow

Propose a dedicated native/public tester client: authorization code + PKCE S256, no embedded shared client secret, explicit loopback callback, `openid profile` plus `api`, and optional `offline_access` if configured. Confirm issuer/discovery metadata and redirect support with the identity provider. Validate callback state, nonce and issuer using a maintained OIDC library.

Existing clients are not automatically suitable. Register an explicit public native client and callback with your provider.

Browser login preserves existing password/MFA/external-provider flows. Account switching requests a fresh login where supported; verify actual behavior against the custom login UI. If the browser returns an existing user, show the returned identity and require the intended profile match instead of relabeling that user.

## Named identities

Examples: `client-admin`, `read-only-user`, `other-client-user`, `service-token`. Cases refer to these aliases and optionally expected subject/tenant/role constraints. The actual user mapping is environment-local.

Maintain multiple independent token sessions. Switching profiles selects a stored valid session or starts login; it does not log all users out. Show user, tenant, authentication type and expiry before Send. Record safe identity facts and their source in every attempt. Decoded JWT claims are diagnostic until validated; never treat decoding alone as validation.

Refresh once per session through a shared lock. Replace rotated refresh/access tokens atomically, handle expiry/revocation by requiring login, and do not rely on provider-specific refresh grace behavior. Disable automatic refresh for explicit expired-token tests. JWT permissions are snapshots; refreshing or reauthenticating may be necessary after server-side role changes.

## Editable authentication headers

Expose generated headers with a source label and masked values. Allow an explicit per-attempt override/removal for negative tests. Avoid silent collisions between manual Authorization and profile-generated Authorization; show which value is effective. A conflict requires a choice, not duplicate accidental headers. Record the override without recording secrets.

## Acceptance

- Login as A and B, run successive cases under each, and verify recorded subjects/tenants.
- Exercise gateway API tokens after the gateway contract is verified, plus raw identity-provider API tokens separately.
- Expired/revoked sessions block normally; deliberate invalid-token tests reach the server and evaluate expected rejection.
- Browser cancel, wrong returned user, refresh rotation and concurrent refresh produce deterministic recoverable behavior.
- No passwords, access/refresh tokens or client secrets appear in reports or MCP responses.
