using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duende.IdentityModel.OidcClient.Browser;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.IdentityModel.Tokens;

internal static class AuthenticationTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string apiUrl)
    {
        using var provider = new TestIdentityProvider();
        var settings = new BrowserLoginSettings(provider.Url, "e2e-tester", "openid profile offline_access", "http://127.0.0.1:7890/callback/", AllowLocalHttp: true);
        var session = await BrowserTokenSession.LoginAsync(settings, provider);
        check(session.Identity.Subject == "user-a" && session.Identity.Source == "validated_oidc_id_token", "OIDC validates signed identity, issuer, audience and nonce");
        check(provider.PkceVerified && !provider.FreshLoginRequested, "OIDC uses S256 PKCE without forcing browser reauthentication");
        var identities = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => session.GetIdentityAsync("admin", true)));
        check(provider.RefreshCount == 1 && identities.Select(i => i.Secret).Distinct().Count() == 1, "concurrent token refresh rotates once and replaces atomically");
        provider.Subject = "user-b";
        var other = await BrowserTokenSession.LoginAsync(settings, provider);
        check(other.Identity.Subject == "user-b" && session.Identity.Subject == "user-a", "separate profiles retain separate verified users");
        await reject(() => BrowserTokenSession.LoginAsync(settings with { ExpectedSubject = "user-a" }, provider), "unexpected browser account is rejected");
        foreach (var fault in new[] { "state", "nonce", "issuer", "audience", "signature", "expired", "unsigned", "missing-id" })
        {
            provider.Fault = fault;
            await reject(() => BrowserTokenSession.LoginAsync(settings, provider), $"OIDC rejects {fault} fault");
        }
        provider.Fault = "";
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await reject(() => BrowserTokenSession.LoginAsync(settings, provider, cancelled.Token), "cancelled login installs no session");
        }
        await reject(() => BrowserTokenSession.LoginAsync(settings with { Authority = "http://example.test", AllowLocalHttp = true }, provider), "development HTTP cannot target a remote authority");
        await reject(() => BrowserTokenSession.LoginAsync(settings with { RedirectUri = "http://0.0.0.0:7890/callback/" }, provider), "callback cannot listen on public interfaces");
        var before = provider.RefreshCount;
        var deliberate = await other.GetIdentityAsync("other", false);
        check(provider.RefreshCount == before && deliberate.Verified?.Subject == "user-b", "explicit negative-token mode suppresses refresh");
        provider.Fault = "refresh-subject";
        await reject(() => other.GetIdentityAsync("other", true), "refresh cannot switch subject");
        before = provider.RefreshCount;
        await reject(() => other.GetIdentityAsync("other", true), "failed refresh requires fresh login");
        check(provider.RefreshCount == before, "failed rotating refresh is not replayed");
        provider.Fault = "";
        var revoked = await BrowserTokenSession.LoginAsync(settings, provider);
        provider.Fault = "revoked";
        await reject(() => revoked.GetIdentityAsync("revoked", true), "revoked refresh stops before API execution");
        provider.Fault = "";

        var root = Path.Combine(Path.GetTempPath(), "E2ETester-auth", Guid.NewGuid().ToString("N"));
        using var store = new SqliteRunStore(root);
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var test = new TestCase { Id = "identity", Request = new() { Url = "/inspect", IdentityRef = "admin" }, Assertions = [new()] };
        var run = await coordinator.StartAsync(new TestPackage { Cases = [test] }, apiUrl);
        var attempt = await coordinator.ExecuteAsync(run, test, test.Request, test.Assertions, identities[0], default);
        var report = Reports.Json(run);
        check(attempt.VerifiedIdentity?.Subject == "user-a" && report.Contains("validated_oidc_id_token"), "attempt reports safe verified identity facts");
        check(report.Contains(identities[0].Secret) && !report.Contains("refresh-canary"), "captured bearer retained without exporting untransmitted refresh credentials");
        check((await store.LoadAsync(run.Id))!.Attempts[0].VerifiedIdentity?.Subject == "user-a", "verified identity facts survive journal reload");

        var callback = $"http://127.0.0.1:{FreePort()}/callback/";
        Task? callbackRequest = null;
        using var http = new HttpClient();
        var loopback = new LoopbackBrowser(_ => callbackRequest = http.GetAsync(callback + "?code=test&state=test"));
        var received = await loopback.InvokeAsync(new BrowserOptions(provider.Url + "/authorize", callback));
        if (callbackRequest is not null) await callbackRequest;
        check(received.ResultType == BrowserResultType.Success && received.Response.Contains("code=test"), "system-browser adapter captures exact loopback callback");
        using (var cancel = new CancellationTokenSource(100))
            await reject(() => new LoopbackBrowser(_ => { }).InvokeAsync(new BrowserOptions(provider.Url, callback), cancel.Token), "waiting browser callback can be cancelled");
        using var released = new HttpListener(); released.Prefixes.Add(callback); released.Start();
        check(released.IsListening, "callback listener releases its port after cancellation");
    }

    private static int FreePort() { var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port; }

    private sealed class TestIdentityProvider : IBrowser, IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly RSA rsa = RSA.Create(2048);
        private readonly ConcurrentDictionary<string, Grant> codes = new(), refreshes = new();
        public string Url { get; } = $"http://127.0.0.1:{FreePort()}";
        public string Subject { get; set; } = "user-a";
        public string Fault { get; set; } = "";
        public bool PkceVerified { get; private set; }
        public bool FreshLoginRequested { get; private set; }
        public int RefreshCount;
        public TestIdentityProvider() { listener.Prefixes.Add(Url + "/"); listener.Start(); _ = ListenAsync(); }
        public Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = Parse(new Uri(options.StartUrl).Query);
            FreshLoginRequested = query.TryGetValue("prompt", out var prompt) && prompt == "login";
            var code = Guid.NewGuid().ToString("N");
            codes[code] = new(Subject, query["nonce"], query["code_challenge"], query["redirect_uri"]);
            return Task.FromResult(new BrowserResult { ResultType = BrowserResultType.Success, Response = options.EndUrl + "?code=" + code + "&state=" + (Fault == "state" ? "wrong" : query["state"]) });
        }
        private async Task ListenAsync()
        {
            while (listener.IsListening)
            {
                try { var context = await listener.GetContextAsync(); _ = HandleAsync(context); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            }
        }
        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                object result;
                switch (context.Request.Url!.AbsolutePath)
                {
                    case "/.well-known/openid-configuration":
                        result = new { issuer = Url, authorization_endpoint = Url + "/authorize", token_endpoint = Url + "/token", jwks_uri = Url + "/jwks", response_types_supported = new[] { "code" }, subject_types_supported = new[] { "public" }, id_token_signing_alg_values_supported = new[] { "RS256" }, code_challenge_methods_supported = new[] { "S256" }, token_endpoint_auth_methods_supported = new[] { "none" } }; break;
                    case "/jwks":
                        var key = rsa.ExportParameters(false);
                        result = new { keys = new[] { new { kty = "RSA", use = "sig", kid = "test-key", alg = "RS256", n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } } }; break;
                    case "/token":
                        using (var reader = new StreamReader(context.Request.InputStream))
                        {
                            var form = Parse(await reader.ReadToEndAsync());
                            var refreshing = form["grant_type"] == "refresh_token";
                            Grant? grant;
                            if (refreshing)
                            {
                                Interlocked.Increment(ref RefreshCount);
                                if (!refreshes.TryRemove(form["refresh_token"], out grant) || Fault == "revoked") { context.Response.StatusCode = 400; result = new { error = "invalid_grant" }; break; }
                            }
                            else
                            {
                                if (!codes.TryRemove(form["code"], out grant)) throw new InvalidOperationException();
                                PkceVerified = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))) == grant.Challenge && form["redirect_uri"] == grant.Redirect && form["client_id"] == "e2e-tester";
                                if (!PkceVerified) { context.Response.StatusCode = 400; result = new { error = "invalid_grant" }; break; }
                            }
                            var refresh = "refresh-canary-" + Guid.NewGuid().ToString("N"); refreshes[refresh] = grant;
                            var claims = new[] { new Claim("sub", Fault == "refresh-subject" && refreshing ? "different-user" : grant.Subject), new Claim("nonce", Fault == "nonce" ? "wrong" : grant.Nonce), new Claim("name", "Test " + grant.Subject), new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
                            using var wrongKey = RSA.Create(2048);
                            var signing = new SigningCredentials(new RsaSecurityKey(Fault == "signature" ? wrongKey : rsa) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);
                            var jwt = new JwtSecurityToken(Fault == "issuer" ? "https://wrong.test" : Url, Fault == "audience" ? "wrong-client" : "e2e-tester", claims, DateTime.UtcNow.AddHours(-2), Fault == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(5), Fault == "unsigned" ? null : signing);
                            result = new Dictionary<string, object> { ["access_token"] = "access-canary-" + Guid.NewGuid().ToString("N"), ["token_type"] = "Bearer", ["expires_in"] = refreshing ? 3600 : 10, ["refresh_token"] = refresh };
                            if (Fault != "missing-id") ((Dictionary<string, object>)result)["id_token"] = new JwtSecurityTokenHandler().WriteToken(jwt);
                        }
                        break;
                    default: context.Response.StatusCode = 404; result = new { error = "not_found" }; break;
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(result);
                context.Response.ContentType = "application/json"; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
            catch (Exception) { context.Response.Abort(); }
        }
        private static Dictionary<string, string> Parse(string input) => input.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => p.Length > 1 ? WebUtility.UrlDecode(p[1]) : "");
        public void Dispose() { listener.Close(); rsa.Dispose(); }
        private sealed record Grant(string Subject, string Nonce, string Challenge, string Redirect);
    }
}
