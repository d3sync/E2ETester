using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class DiagnosticsTests
{
    public static async Task RunAsync(Action<bool, string> check, string baseUrl)
    {
        var textCheck = new Assertion { Kind = CheckKind.BodyNotEmpty };
        foreach (var (body, expected) in new[] { ("cloud response", Verdict.Pass), ("", Verdict.Fail), (" \r\n", Verdict.Fail) })
            check(Assertions.Evaluate([textCheck], new() { StatusCode = 200, Body = body })[0].Verdict == expected, "non-empty string check " + expected);
        check(Assertions.Evaluate([textCheck], new() { StatusCode = 200, Body = "partial", Truncated = true })[0].Verdict == Verdict.Inconclusive, "non-empty text check rejects incomplete evidence");
        check(Assertions.Evaluate([textCheck], new() { StatusCode = 200, Body = "eA==", BodyEncoding = "base64" })[0].Verdict == Verdict.Inconclusive, "non-empty text check does not treat binary as a string");
        var transport = new HttpTransport();
        var identity = new IdentitySession("swagger", AuthKind.Bearer, "bearer-canary-918");
        var request = new RequestDefinition { Method = "POST", SendBody = true, Body = "apostrophe ' and $(echo test)", Headers = [new() { Name = "X-Trace", Value = "kept" }, new() { Name = "X-Omitted", Value = "gone", Enabled = false }] };
        var response = await transport.SendAsync(request, new Uri(baseUrl + "/bytes?a=1&a=2"), identity, default);
        var sent = response.Requests.Single();
        using (var json = JsonDocument.Parse(response.Body))
            check(Convert.ToBase64String(Encoding.UTF8.GetBytes(sent.Body)) == json.RootElement.GetProperty("base64").GetString(), "recorded body matches received bytes");
        check(sent.Url.EndsWith("?a=1&a=2") && sent.Headers["Host"][0] == new Uri(baseUrl).Authority && !sent.Headers.ContainsKey("X-Omitted"), "request evidence preserves URL and effective headers");
        var safe = PackageJson.Clone(response);
        var curl = RequestDiagnostics.Curl(safe.Requests.Single(), 30);
        check(PackageJson.Write(safe).Contains(identity.Secret) && curl.Contains("Authorization: Bearer " + identity.Secret) && curl.Contains("'\"'\"'") && curl.Contains("$(echo test)"), "cURL preserves credentials and quotes shell data");
        request = request with { BodyKind = RequestBodyKind.Multipart, Parts = [new() { Name = "upload", FileName = "example.txt", Value = "multipart-value" }] };
        response = await transport.SendAsync(request, new Uri(baseUrl + "/bytes"), identity, default);
        using (var json = JsonDocument.Parse(response.Body))
            check(response.Requests[0].Body == json.RootElement.GetProperty("base64").GetString() && response.Requests[0].Headers["Content-Type"][0] == json.RootElement.GetProperty("contentType").GetString(), "multipart dump retains transmitted boundary and bytes");
        check(PackageJson.Clone(response).Requests[0].Body == response.Requests[0].Body, "sensitive multipart evidence is retained");
        request = request with { BodyKind = RequestBodyKind.FormUrlEncoded, Parts = [new() { Name = "password", Value = "sensitive value & more" }] };
        response = await transport.SendAsync(request, new Uri(baseUrl + "/bytes"), identity, default);
        check(PackageJson.Clone(response).Requests[0].Body == "password=sensitive+value+%26+more", "form credentials with spaces are preserved after encoding");
        response = await transport.SendAsync(new() { FollowRedirects = true }, new Uri(baseUrl + "/redirect"), identity, default);
        check(response.Requests.Count == 2 && response.Requests[0].Url.EndsWith("/redirect") && response.Requests[1].Url != response.Requests[0].Url, "redirect requests retained individually");
        response = await transport.SendAsync(new() { TimeoutSeconds = 1 }, new Uri(baseUrl + "/slow"), identity, default);
        check(response.ErrorCode == "timeout" && response.Requests.Count == 1, "timed-out send retains prepared request");
        var swagger = SwaggerTokenCapture.ValidateAddress("https://example.test/swagger/");
        check(SwaggerTokenCapture.FromAuthorization(swagger, "https://example.test/api", "Bearer canary") == "canary", "Swagger same-origin bearer accepted");
        foreach (var url in new[] { "https://example.test.evil/api", "http://example.test/api", "https://example.test:444/api", "https://other.test/api" })
            check(SwaggerTokenCapture.FromAuthorization(swagger, url, "Bearer canary") is null, "Swagger foreign origin rejected " + url);
        foreach (var header in new[] { "Basic canary", "Bearer ", "Bearer bad\r\nheader" })
            check(SwaggerTokenCapture.FromAuthorization(swagger, "https://example.test/api", header) is null, "invalid captured authorization rejected");
    }
}
