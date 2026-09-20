using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using Duende.IdentityModel.OidcClient.Results;
using E2ETester.Core;
using Microsoft.IdentityModel.Tokens;

namespace E2ETester.Infrastructure;

// One instance per identity. All token replacement and refresh is serialized; no HTTP request retries.
public sealed class BrowserTokenSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly OidcClient client;
    private readonly SignedIdentityValidator validator;
    private string accessToken, refreshToken;
    private bool invalidated;
    public bool RequiresLogin => invalidated;
    public VerifiedIdentity Identity { get; private set; }
    private BrowserTokenSession(OidcClient client, SignedIdentityValidator validator, LoginResult result)
    {
        this.client = client; this.validator = validator;
        accessToken = result.AccessToken; refreshToken = result.RefreshToken ?? "";
        Identity = new(client.Options.Authority, validator.Subject!, result.User.FindFirst("name")?.Value ?? validator.Subject!, result.AccessTokenExpiration);
    }

    public static async Task<BrowserTokenSession> LoginAsync(BrowserLoginSettings settings, IBrowser? browser = null, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var nonce = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var validator = new SignedIdentityValidator(nonce, settings.ExpectedSubject);
        var options = new OidcClientOptions
        {
            Authority = settings.Authority.TrimEnd('/'), ClientId = settings.ClientId, Scope = settings.Scope,
            RedirectUri = settings.RedirectUri, Browser = browser ?? new LoopbackBrowser(),
            IdentityTokenValidator = validator, LoadProfile = false, ClockSkew = TimeSpan.FromSeconds(30),
            Policy = new Policy { RequireIdentityTokenSignature = true, Discovery = new DiscoveryPolicy { RequireHttps = !settings.AllowLocalHttp } },
            BackchannelHandler = new HttpClientHandler { AllowAutoRedirect = false },
            BackchannelTimeout = TimeSpan.FromSeconds(30)
        };
        var client = new OidcClient(options);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            var result = await client.LoginAsync(new LoginRequest { FrontChannelExtraParameters = new Parameters { { "nonce", nonce }, { "prompt", "login" } } }, deadline.Token);
            if (result.IsError || string.IsNullOrEmpty(result.IdentityToken) || validator.Subject is null || result.AccessTokenExpiration <= DateTimeOffset.UtcNow || result.TokenResponse.TokenType?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidOperationException();
            validator.Refresh = true;
            return new(client, validator, result);
        }
        catch (OperationCanceledException) { throw new InvalidOperationException("Browser login was cancelled or timed out. No identity was installed."); }
        catch (Exception) { throw new InvalidOperationException("Browser login failed validation. Check the authority, client registration, callback, scopes and expected subject. No identity was installed."); }
    }

    public async Task<IdentitySession> GetIdentityAsync(string alias, bool automaticRefresh, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (invalidated) throw new InvalidOperationException("This browser session requires a new login.");
            if (automaticRefresh && Identity.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            {
                if (refreshToken.Length == 0) throw new InvalidOperationException("The browser session is expiring. Sign in again; no refresh token was issued.");
                try
                {
                    var result = await client.RefreshTokenAsync(refreshToken, cancellationToken: cancellationToken);
                    if (result.IsError || string.IsNullOrEmpty(result.AccessToken) || result.AccessTokenExpiration <= DateTimeOffset.UtcNow) throw new InvalidOperationException();
                    accessToken = result.AccessToken;
                    if (!string.IsNullOrEmpty(result.RefreshToken)) refreshToken = result.RefreshToken;
                    Identity = Identity with { ExpiresAt = result.AccessTokenExpiration };
                }
                catch (Exception)
                {
                    // A cancelled/failed refresh may already have consumed a one-time token. Do not retry it.
                    invalidated = true; accessToken = refreshToken = "";
                    throw new InvalidOperationException("Token refresh failed or was interrupted. Sign in again before sending; the API request was not sent.");
                }
            }
            return new(alias, AuthKind.Bearer, accessToken, Verified: Identity);
        }
        finally { gate.Release(); }
    }

    public static void Validate(BrowserLoginSettings settings)
    {
        if (!Uri.TryCreate(settings.Authority, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.Scheme != "https" && !(settings.AllowLocalHttp && uri.Scheme == "http" && uri.Host == "127.0.0.1")))
            throw new InvalidOperationException("Use an HTTPS authority. Development HTTP is allowed only on 127.0.0.1 with explicit opt-in.");
        if (settings.AllowLocalHttp && uri.Host != "127.0.0.1") throw new InvalidOperationException("Local development mode is restricted to 127.0.0.1.");
        if (string.IsNullOrWhiteSpace(settings.ClientId) || !settings.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid")) throw new InvalidOperationException("Enter the registered client ID and include the openid scope.");
        LoopbackBrowser.ValidateRedirect(settings.RedirectUri);
    }

    private sealed class SignedIdentityValidator(string nonce, string expectedSubject) : IIdentityTokenValidator
    {
        public bool Refresh { get; set; }
        public string? Subject { get; private set; }
        public Task<IdentityTokenValidationResult> ValidateAsync(string token, OidcClientOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                var keys = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(System.Text.Json.JsonSerializer.Serialize(options.ProviderInformation.KeySet)).GetSigningKeys();
                var principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKeys = keys,
                    ValidateIssuer = true, ValidIssuer = options.Authority, ValidateAudience = true, ValidAudience = options.ClientId,
                    ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = options.ClockSkew,
                    ValidAlgorithms = options.Policy.ValidSignatureAlgorithms
                }, out var securityToken);
                var jwt = (JwtSecurityToken)securityToken;
                var subject = principal.FindFirst("sub")?.Value;
                var returnedNonce = principal.FindFirst("nonce")?.Value;
                var azp = principal.FindFirst("azp")?.Value;
                var issuedAt = principal.FindFirst("iat")?.Value;
                if (!long.TryParse(issuedAt, out var issuedSeconds) || issuedSeconds > DateTimeOffset.UtcNow.Add(options.ClockSkew).ToUnixTimeSeconds() || principal.FindAll("sub").Count() != 1 || principal.FindAll("nonce").Count() > 1 || string.IsNullOrEmpty(subject) || (!Refresh && returnedNonce != nonce) || (Refresh && returnedNonce is not null && returnedNonce != nonce) || (expectedSubject.Length > 0 && subject != expectedSubject) || (Subject is not null && subject != Subject) || (jwt.Audiences.Count() > 1 && azp != options.ClientId) || (azp is not null && azp != options.ClientId))
                    return Task.FromResult(new IdentityTokenValidationResult { Error = "identity_mismatch" });
                Subject = subject;
                return Task.FromResult(new IdentityTokenValidationResult { User = principal, SignatureAlgorithm = jwt.Header.Alg });
            }
            catch (SecurityTokenSignatureKeyNotFoundException) { return Task.FromResult(new IdentityTokenValidationResult { Error = "invalid_signature" }); }
            catch (Exception) { return Task.FromResult(new IdentityTokenValidationResult { Error = "invalid_identity_token" }); }
        }
    }
}
