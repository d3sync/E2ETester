using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class TransportTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string baseUrl)
    {
        var transport = new HttpTransport(); var identity = new IdentitySession("none", AuthKind.None);
        var request = new RequestDefinition { FollowRedirects = true };
        var response = await transport.SendAsync(request, new Uri(baseUrl + "/redirect"), identity, default);
        check(response.StatusCode == 200 && response.Redirects.Count == 1 && response.Redirects[0].StatusCode == 302, "same-origin redirect records chain and final response");
        response = await transport.SendAsync(request, new Uri(baseUrl + "/redirect-external"), identity, default);
        check(response.StatusCode == 302 && response.ErrorCode == "cross_origin_redirect", "cross-origin redirect is stopped before forwarding credentials");
        response = await transport.SendAsync(request with { MaxRedirects = 2 }, new Uri(baseUrl + "/redirect-loop"), identity, default);
        check(response.ErrorCode == "redirect_limit" && response.Redirects.Count == 3, "redirect loops are bounded with recorded stop");
        response = await transport.SendAsync(request with { HttpVersion = "2.0" }, new Uri(baseUrl + "/health"), identity, default);
        check(response.StatusCode == 200 && response.HttpVersion == "1.1", "HTTP version preference records negotiated fallback");
        response = await transport.SendAsync(request with { ClientCertificateThumbprint = new string('0', 40) }, new Uri(baseUrl + "/health"), identity, default);
        check(response.ErrorCode == "request_error" && response.StatusCode is null, "missing local client certificate blocks send");
        await reject(() => { PackageJson.Validate(new() { Cases = [new() { Request = request with { ProxyUrl = "http://user:password@localhost" } }] }); return Task.CompletedTask; }, "credential-bearing proxy rejected");
        var sample = new ResponseSnapshot { StatusCode = 201, DurationMs = 12, Headers = new() { ["X-Test"] = ["yes"] }, Body = "{\"n\":3,\"items\":[1,2],\"nullable\":null}" };
        foreach (var assertion in new[] { new Assertion { Kind = CheckKind.StatusIn, Expected = "[200,201]" }, new() { Kind = CheckKind.HeaderExists, Selector = "x-test" }, new() { Kind = CheckKind.JsonExists, Selector = "/nullable" }, new() { Kind = CheckKind.JsonGreaterThan, Selector = "/n", Expected = "2" }, new() { Kind = CheckKind.JsonLessThan, Selector = "/n", Expected = "4" }, new() { Kind = CheckKind.JsonArrayCount, Selector = "/items", Expected = "2" }, new() { Kind = CheckKind.DurationLessThan, Expected = "20" } })
            check(Assertions.Evaluate([assertion], sample)[0].Verdict == Verdict.Pass, "extended assertion " + assertion.Kind);
        check(Assertions.Evaluate([new() { Kind = CheckKind.JsonExists, Selector = "/missing" }], sample)[0].Verdict == Verdict.Fail, "missing JSON value fails existence check");
        check(Assertions.Evaluate([new() { Kind = CheckKind.JsonArrayCount, Selector = "/items", Expected = "2" }], sample with { Truncated = true })[0].Verdict == Verdict.Inconclusive, "extended JSON checks respect truncated evidence");
    }
}
