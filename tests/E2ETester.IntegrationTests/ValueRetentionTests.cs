using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class ValueRetentionTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "E2E-value-retention", Guid.NewGuid().ToString("N"));
        const string body = "{\"groupToken\":\"group-123\",\"token\":\"created-456\",\"nested\":{\"password\":\"test-value\"}}";
        var test = new TestCase { Id = "create", Request = new() { Url = "/create", SendBody = true, Body = body, BodySensitive = true,
            Query = [new() { Name = "api_key", Value = "query-value", Sensitive = true }] },
            Assertions = [new() { Kind = CheckKind.JsonEquals, Selector = "/groupToken", Expected = "\"group-123\"" }] };
        var action = new TestCase { Id = "cleanup", Request = new() { Method = "DELETE", Url = "/items/{{case.create./token}}" } };
        var package = new TestPackage { SchemaVersion = "1.6", Cases = [test], GuidedActions = [action] };
        string id;
        using (var store = new SqliteRunStore(root))
        {
            var coordinator = new RunCoordinator(store, new CapturedTransport(body));
            var run = await coordinator.StartAsync(package, "https://example.test"); id = run.Id;
            var attempt = await coordinator.ExecuteAsync(run, test, test.Request, test.Assertions, new("none", AuthKind.None), default);
            check(attempt.Verdict == Verdict.Pass && attempt.Checks[0].Actual.Contains("group-123"), "named token assertions retain actual and expected values");
            check(attempt.Response!.Body == body && attempt.Request.Body == body, "sensitive body metadata never alters request or response bytes");
            check(attempt.Response.Headers["Set-Cookie"][0] == "sid=cookie-789", "response cookies retained exactly");
            check(attempt.Request.Query[0].Value == "query-value" && run.Package.Cases[0].Request.Body == body, "package and request retain sensitive fields");
        }
        using (var store = new SqliteRunStore(root))
        {
            var run = (await store.LoadAsync(id))!;
            check(GuidedActionBindings.Resolve(action.Request, run).Url == "/items/created-456", "Guided Action can reuse a saved token after reopening workspace");
            check(RequestDiagnostics.Curl(run.Attempts[0].Response!.Requests[0], 30).Contains("Bearer captured-token"), "reloaded cURL retains authentication header");
            check(new[] { Reports.Json(run), Reports.Text(run), Reports.Html(run) }.All(s => s.Contains("group-123") && s.Contains("captured-token")), "all report formats retain captured values");
        }
        var query = new QueryDefinition { SensitiveColumns = ["groupToken"] };
        var evidence = DatabaseQueries.SafeEvidence(query, new QueryResult { Rows = [new() { ["groupToken"] = JsonSerializer.SerializeToElement("group-123") }] });
        var result = DatabaseQueries.Assess(new DatabaseCheck { Kind = DatabaseCheckKind.ScalarEquals, Column = "groupToken", ExpectedJson = "\"group-123\"" }, evidence);
        check(result.Verdict == Verdict.Pass, "sensitive database columns remain comparable");
    }
    private sealed class CapturedTransport(string body) : IHttpTransport
    {
        public Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri url, IdentitySession identity, CancellationToken token) => Task.FromResult(new ResponseSnapshot
        {
            StatusCode = 200, Body = body, Headers = new() { ["Set-Cookie"] = ["sid=cookie-789"] },
            Requests = [new() { Url = url.AbsoluteUri, Method = request.Method, Body = body, HasBody = true, Headers = new() { ["Authorization"] = ["Bearer captured-token"] } }]
        });
    }
}
