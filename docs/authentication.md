# Browser sign-in and API tokens

In the **Auth** tab, add an identity with **+**. Each profile holds a separate in-memory session. Configure the authority, registered client ID, scopes and callback, then choose **Sign in with browser**. The system browser handles passwords, MFA and external providers. The tester never collects passwords.

After login, review the displayed issuer and subject before sending. The subject is pinned into **Expected subject** so a subsequent login cannot silently substitute another account. Use a separate profile to test another user. Settings edits apply to the next login; existing sessions remain bound to the configuration that created them. The tester requests `prompt=login`, but actual account selection and MFA behavior depend on your identity provider's login UI.

**Refresh before sending** renews a session when the token expires within 30 seconds. Refresh is serialized per profile, rotated tokens replace the old values together, and a failed or interrupted refresh requires login again. Requests are never retried after authentication failure. Turn refresh off explicitly to test the server's behavior for an expired access token. **Forget login** removes the tester session, without logging other profiles or the browser out.

Expand **Manual JWT or API token** to paste an existing token. These tokens are not treated as verified identities and do not refresh. Bearer adds `Bearer ` automatically. Configure the token header and prefix according to your API documentation.

Browser sessions and refresh tokens remain in memory. Manual JWT/API tokens can be saved explicitly with a local environment using Windows user encryption; captured request headers and echoes are included verbatim in evidence and exports. Swagger import is described in [the walkthrough](swagger-diagnostics-themes.md). Attempts record the verified subject, issuer, display name, access-token expiry and provenance (`validated_oidc_id_token`), never the ID token itself. These facts come from the validated ID token, not from decoding arbitrary access-token claims. Tenant/role expectations and access-token audience inspection remain future work.

## Registering a native client

Register a dedicated public native client with your identity provider. This generic IdentityServer-style example must be adapted to your environment:

```csharp
new Client
{
    ClientId = "e2e-tester",
    ClientName = "E2E Tester desktop",
    AllowedGrantTypes = GrantTypes.Code,
    RequireClientSecret = false,
    RequirePkce = true,
    AllowPlainTextPkce = false,
    RedirectUris = { "http://127.0.0.1:7890/callback/" },
    AllowedScopes = { "openid", "profile", "api" },
    AllowOfflineAccess = true,
    RefreshTokenUsage = TokenUsage.OneTimeOnly,
    RefreshTokenExpiration = TokenExpiration.Sliding
};
```

Add an exact redirect match for this client in the custom redirect validator. Register another explicit port if 7890 is occupied, then use the same URI in the tester. The callback path must be `/callback/`, the host `127.0.0.1`, and the port at least 1024. No client secret belongs in this desktop application. No logout callback is needed for the current local **Forget login** action. Server-side token revocation/global logout are not implemented.

Use the environment's actual HTTPS issuer as Authority. Scopes default to `openid profile api offline_access`; omit `offline_access` if refresh is not enabled. **Allow HTTP for local development** only permits an authority on literal `127.0.0.1`. It does not bypass TLS certificate validation for HTTPS.

## Validation and implementation

### Managing identities

On the suite sign-in step, **Sign in with Swagger → Read authorized token → Use token for selected identity** imports the token and records the sign-in step automatically. Reading alone previews a token; **Use token** installs it. Continue to remote-IP validation to verify server access. Changing the sign-in identity also changes its paired validation request when they previously shared an identity; explicitly different identities on other tests remain separate.

Open **Auth → Manage** or **Tools → Manage identities** to add, rename or delete identities, or clear their tokens. Authentication settings remain editable in the Auth tab. Use **Workspace → Save environment** to persist new identities and credentials with Windows user encryption.

Deleting an identity used by tests requires choosing a replacement. Sign-in steps require a named replacement. Renaming/deleting updates current test references and matching saved identities in this workspace; historical attempts keep the identity names recorded when they ran. The built-in `none` identity cannot be renamed or deleted.

Clearing a token also clears its native login, saved token and remembered token. Browser cookies are separate and remain available; use **Clear saved sign-in data** in Swagger to remove them. Renaming preserves the original browser profile.

### Remembering sessions across releases

Swagger imports and manually supplied tokens used for requests are automatically remembered under `%LOCALAPPDATA%\E2ETester\Sessions\identities.protected`, encrypted with Windows DPAPI for the current user. Identities and tokens are restored only for their saved API origin (scheme, host and port). New default suites reuse a remembered identity; imported suites retain their explicit identity references. No request runs automatically.

WebView2 cookies, site storage and saved-password settings remain under `%LOCALAPPDATA%\E2ETester\Browser`. This existing folder is intentionally retained, independent of executable path, single-file extraction, installation or application version. Renaming identities retains their browser profile key. These directories are shared by installed and portable releases under the same Windows account.

Existing in-memory tokens from older releases become remembered when imported or used in this version. Saved access tokens still expire; obtain a new token through Swagger when needed. Microsoft/your identity provider can require MFA or sign-in again according to its policies. Native registered-client OIDC refresh sessions remain in memory. Use Manage identities to clear remembered tokens or delete identities, and Swagger's clear action to remove cookies separately.

The implementation uses [Duende's Apache-2.0 OIDC client](https://docs.duendesoftware.com/identitymodel-oidcclient/) for authorization-code/PKCE, state handling, discovery and token exchange. A signature validator backed by Microsoft's JWT validation library enforces signature algorithms, signing keys, issuer, audience, lifetime, issued-at, nonce, authorized party and expected subject. Refresh cannot replace the original subject. Raw provider errors and token payloads are not displayed or logged.

The integration suite runs a local RSA-signed identity provider and verifies successful login, PKCE, fresh-login requests, separate users, wrong-account rejection, state/nonce/issuer/audience/signature/expiry rejection, unsigned/missing ID tokens, refresh concurrency/rotation/revocation, report value retention and the real loopback listener's cleanup/cancellation. This is controlled protocol testing, not evidence of compatibility with any particular deployed identity provider.

Run `dotnet run --project tests/E2ETester.IntegrationTests`. The desktop `--smoke=artifacts/authentication.png --authentication-exercise` checks configuration failure behavior and renders the Auth tab without contacting an identity provider.

## Reusable permission identities (0.7.22)

Use stable named identities for distinct permission sets: `cloud-user` for normal/full-access tests, `cloud-clientadmin` for ClientAdmin, and separate descriptive aliases for other exact flag combinations (for example `cloud-clientadmin-telephony`). Reuse the same alias for every case with that permission set, even when all roles belong to one database account. Never flatten role aliases to cloud-user or generate timestamp identities. A valid JWT whose configured claim equals the required value satisfies permission preparation across cases, suites and restarts. Prepare and sign in only when that role token is unavailable, expired or mismatched. Restoring the database does not invalidate already-issued JWTs. Ordinary non-permission data preparation remains scoped to its operation.

Stored bearer tokens expire at JWT exp, with a fixed 24-hour fallback for tokens without exp. The decoded claim check is a workflow comparison, not signature validation. The API validates authorization.
