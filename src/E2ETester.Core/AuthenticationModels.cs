namespace E2ETester.Core;

public sealed record VerifiedIdentity(string Issuer, string Subject, string DisplayName, DateTimeOffset ExpiresAt, string Source = "validated_oidc_id_token");

// Configuration contains no credentials. Tokens are held only by the runtime session.
public sealed record BrowserLoginSettings(string Authority, string ClientId, string Scope, string RedirectUri, string ExpectedSubject = "", bool AllowLocalHttp = false);
