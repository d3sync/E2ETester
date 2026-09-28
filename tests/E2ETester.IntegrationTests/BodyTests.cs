using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class BodyTests
{
    public static async Task RunAsync(Action<bool, string> check, string baseUrl)
    {
        var transport = new HttpTransport(); var identity = new IdentitySession("none", AuthKind.None);
        var request = new RequestDefinition { Url = "/bytes", Method = "POST", SendBody = true, BodyKind = RequestBodyKind.Binary, Body = Convert.ToBase64String([0, 255, 128, 13, 10]), ContentType = "application/octet-stream" };
        var result = await transport.SendAsync(request, new Uri(baseUrl + "/bytes"), identity, default);
        using (var doc = JsonDocument.Parse(result.Body)) check(doc.RootElement.GetProperty("base64").GetString() == request.Body, "binary upload preserves exact bytes");
        request = request with { BodyKind = RequestBodyKind.Multipart, Parts = [new() { Name = "text", Value = "hello" }, new() { Name = "file", FileName = "test.bin", Binary = true, Value = Convert.ToBase64String([0, 255, 128]) }, new() { Name = "omitted", Value = "hidden", Enabled = false }] };
        result = await transport.SendAsync(request, new Uri(baseUrl + "/bytes"), identity, default);
        using (var doc = JsonDocument.Parse(result.Body))
        {
            var bytes = Convert.FromBase64String(doc.RootElement.GetProperty("base64").GetString()!);
            var text = Encoding.UTF8.GetString(bytes);
            check(bytes.AsSpan().IndexOf(new byte[] { 0, 255, 128 }) >= 0 && text.Contains("test.bin") && text.Contains("hello") && !text.Contains("hidden"), "multipart transmits file bytes, fields and omission");
            check(doc.RootElement.GetProperty("contentType").GetString()!.Contains("boundary="), "multipart boundary matches generated content type");
        }
        request = request with { BodyKind = RequestBodyKind.FormUrlEncoded, Parts = [new() { Name = "a", Value = "x & y" }, new() { Name = "a", Value = "" }] };
        result = await transport.SendAsync(request, new Uri(baseUrl + "/bytes"), identity, default);
        using (var doc = JsonDocument.Parse(result.Body)) check(Encoding.UTF8.GetString(Convert.FromBase64String(doc.RootElement.GetProperty("base64").GetString()!)) == "a=x+%26+y&a=", "URL-encoded form retains duplicates and empty values");
        result = await transport.SendAsync(new(), new Uri(baseUrl + "/binary"), identity, default);
        check(result.BodyEncoding == "base64" && result.Body == Convert.ToBase64String([0, 255, 128, 13, 10]), "binary response preserves bytes with encoding marker");
        check(Assertions.Evaluate([new() { Kind = CheckKind.BodyContains, Expected = "" }], result)[0].Verdict == Verdict.Inconclusive, "binary response cannot pass text assertion");
        var cookie = new RequestDefinition { UseCookies = true, SessionScope = "first" };
        await transport.SendAsync(cookie, new Uri(baseUrl + "/set-cookie"), identity, default);
        result = await transport.SendAsync(cookie, new Uri(baseUrl + "/cookie"), identity, default);
        check(result.Body.Contains("cookie-canary-123"), "cookie session persists across requests");
        check(PackageJson.Write(PackageJson.Clone(result)).Contains("cookie-canary-123"), "cookie echo preserved even without a Set-Cookie response");
        result = await transport.SendAsync(cookie with { SessionScope = "second" }, new Uri(baseUrl + "/cookie"), identity, default);
        check(result.Body == "none", "cookies isolated between runs");
        result = await transport.SendAsync(cookie, new Uri(baseUrl + "/cookie"), identity with { Alias = "another" }, default);
        check(result.Body == "none", "cookies isolated between identities");
        var secret = Convert.ToBase64String(Encoding.UTF8.GetBytes("binary-secret-canary"));
        check(PackageJson.Clone(new ResponseSnapshot { Body = secret, BodyEncoding = "base64" }).Body == secret, "known secrets in binary responses are retained");
        using var store = new SqliteRunStore(Path.Combine(Path.GetTempPath(), "E2ETester-omitted-body", Guid.NewGuid().ToString("N")));
        var coordinator = new RunCoordinator(store, transport);
        var omitted = new TestCase { Request = new() { Url = "/health", SendBody = false, Body = "[REDACTED]", Parts = [new() { Value = "{{case.unavailable./id}}" }] }, Assertions = [new()] };
        var run = await coordinator.StartAsync(new() { Cases = [omitted] }, baseUrl);
        check((await coordinator.ExecuteAsync(run, omitted, omitted.Request, omitted.Assertions, identity, default)).Verdict == Verdict.Pass, "omitted body does not require inactive secrets or capture bindings");
    }
}
