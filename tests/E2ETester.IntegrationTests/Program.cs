using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

if (args.Contains("--serve"))
{
    using var demo = new TestServer(5188);
    Console.WriteLine("Local demo: http://localhost:5188. Close this process to stop.");
    await Task.Delay(Timeout.Infinite); return;
}
var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    count++; Console.WriteLine("PASS: " + name);
}
async Task Reject(Func<Task> action, string name)
{
    try { await action(); } catch (Exception) { Check(true, name); return; }
    throw new Exception("FAIL: expected rejection: " + name);
}
using var server = new TestServer();
var directory = Path.Combine(Path.GetTempPath(), "E2ETester-tests", Guid.NewGuid().ToString("N"));
using var store = new SqliteRunStore(directory);
var coordinator = new RunCoordinator(store, new HttpTransport());
var test = new TestCase { Id = "test", Title = "Wire contract", Request = new() { Url = "/inspect" }, Assertions = [new()] };
var package = new TestPackage { Cases = [test], BaseUrl = server.Url };
var run = await coordinator.StartAsync(package, server.Url);
var none = new IdentitySession("none", AuthKind.None);
var draft = PackageJson.Clone(test.Request);
draft.Method = "PATCH"; draft.SendBody = true; draft.Body = "{\"number\":12}";
draft.Query = [new() { Name = "tag", Value = "one" }, new() { Name = "tag", Value = "two & three" }, new() { Name = "omit", Value = "hidden", Required = true, Enabled = false }];
draft.Headers = [new() { Name = "X-Test", Value = "original" }];
var first = await coordinator.ExecuteAsync(run, test, draft, test.Assertions, none, default);
Check(first.Verdict == Verdict.Pass, "HTTP status assertion passes");
var sent = server.Requests.Last();
Check(sent.Method == "PATCH" && sent.Body == draft.Body, "method and raw body reach server");
Check(sent.RawUrl.Contains("tag=one&tag=two%20%26%20three") && !sent.RawUrl.Contains("omit"), "duplicate query fields and required-field omission");
Check(first.Changes.Count > 0 && first.Request.Headers[0].Value == "original", "attempt snapshots changes");
draft.Headers[0].Value = "later";
Check(first.Request.Headers[0].Value == "original", "later edits cannot rewrite attempt");
var denied = draft with { Url = "/denied", SendBody = false };
var rejection = await coordinator.ExecuteAsync(run, test, denied, [new() { Expected = "403" }], none, default);
Check(rejection.Verdict == Verdict.Pass && rejection.Response?.StatusCode == 403, "expected rejection is PASS");
var failure = await coordinator.ExecuteAsync(run, test, denied, [new()], none, default);
Check(failure.Verdict == Verdict.Fail && Outcomes.ForRun(run) == Verdict.Fail, "unexpected status is FAIL");
var noChecks = await coordinator.ExecuteAsync(run, test, test.Request, [], none, default);
Check(noChecks.Verdict == Verdict.Inconclusive, "no assertions cannot pass");
var jsonResponse = new ResponseSnapshot { StatusCode = 200, Body = "{\"a/b\":{\"~name\":[12,null,\"12\"]}}" };
Check(Assertions.Evaluate([new() { Kind = CheckKind.JsonEquals, Selector = "/a~1b/~0name/0", Expected = "12" }], jsonResponse)[0].Verdict == Verdict.Pass, "JSON Pointer escapes and arrays");
Check(Assertions.Evaluate([new() { Kind = CheckKind.JsonEquals, Selector = "/a~1b/~0name/2", Expected = "12" }], jsonResponse)[0].Verdict == Verdict.Fail, "JSON types are not coerced");
Check(Outcomes.Evaluate([new("1", true, Verdict.Pass, "", "", ""), new("2", false, Verdict.Fail, "", "", "")]) == Verdict.Pass, "optional failed check is a warning");
const string token = "canary-jwt-value-4ba09";
var authenticated = test.Request with { IdentityRef = "alice" };
var authAttempt = await coordinator.ExecuteAsync(run, test, authenticated, [new()], new("alice", AuthKind.Bearer, token), default);
Check(server.Requests.Last().Authorization == "Bearer " + token, "bearer token injected on wire");
Check(!PackageJson.Write(authAttempt).Contains(token), "echoed token redacted");
var api = authenticated with { IdentityRef = "service" };
await coordinator.ExecuteAsync(run, test, api, [new()], new("service", AuthKind.ApiToken, token), default);
Check(server.Requests.Last().Authorization == token, "raw identity-provider API-token format");
await coordinator.ExecuteAsync(run, test, api, [new()], new("service", AuthKind.ApiToken, token, "X-API-Key"), default);
Check(server.Requests.Last().ApiKey == token, "configurable API-token header");
var beforeConflict = server.Requests.Count;
await Reject(() => coordinator.ExecuteAsync(run, test, authenticated with { Headers = [new() { Name = "Authorization", Value = "manual" }] }, [new()], new("alice", AuthKind.Bearer, token), default), "authentication collision rejected");
Check(server.Requests.Count == beforeConflict, "collision never reaches network");
var sensitive = test.Request with { Url = "/inspect?api_key=url-canary-882", Method = "POST", SendBody = true, Body = "{\"password\":\"body-canary-772\"}", Headers = [new() { Name = "X-Custom", Value = "field-canary-443", Sensitive = true }] };
var sensitiveAttempt = await coordinator.ExecuteAsync(run, test, sensitive, [new()], none, default);
var serialized = PackageJson.Write(sensitiveAttempt);
Check(!serialized.Contains("url-canary-882") && !serialized.Contains("body-canary-772") && !serialized.Contains("field-canary-443"), "URL, JSON and sensitive-field echoes redacted");
var passwordResponse = await coordinator.ExecuteAsync(run, test, test.Request with { Url = "/secret" }, [new() { Kind = CheckKind.JsonEquals, Selector = "/password", Expected = "\"server-canary-681\"" }], none, default);
Check(passwordResponse.Verdict == Verdict.Pass && !PackageJson.Write(passwordResponse).Contains("server-canary-681"), "response-only secret in check evidence redacted");
var redirect = await coordinator.ExecuteAsync(run, test, test.Request with { Url = "/redirect" }, [new() { Expected = "302" }], none, default);
Check(redirect.Response?.StatusCode == 302 && server.Requests.Last().RawUrl == "/redirect", "redirects not followed");
var capped = new RunCoordinator(store, new HttpTransport(64));
var large = await capped.ExecuteAsync(run, test, test.Request with { Url = "/large" }, [new() { Kind = CheckKind.BodyContains, Expected = "x" }], none, default);
Check(large.Response?.Truncated == true && large.Verdict == Verdict.Inconclusive, "truncated body cannot pass");
var beforeTimeout = server.Requests.Count;
var timed = await coordinator.ExecuteAsync(run, test, test.Request with { Url = "/slow", TimeoutSeconds = 1 }, [new()], none, default);
Check(timed.Response?.ErrorCode == "timeout" && timed.Verdict == Verdict.Inconclusive, "timeout records uncertain delivery");
Check(server.Requests.Count == beforeTimeout + 1, "no automatic retry");
using (var cancel = new CancellationTokenSource(100))
{
    var cancelled = await coordinator.ExecuteAsync(run, test, test.Request with { Url = "/slow" }, [new()], none, cancel.Token);
    Check(cancelled.State == AttemptState.Cancelled && cancelled.Verdict == Verdict.Inconclusive, "cancellation recorded");
}
await Reject(() => Task.Run(() => { using var duplicate = new SqliteRunStore(directory); }), "workspace ownership exclusive");
run.Attempts.Add(new Attempt { CaseId = test.Id }); await store.SaveAsync(run);
var beforeResume = server.Requests.Count;
var resumed = await coordinator.ResumeAsync();
Check(resumed?.Attempts.Last().State == AttemptState.Interrupted && server.Requests.Count == beforeResume, "recovery without replay");
Check(resumed!.Attempts.Count == run.Attempts.Count, "SQLite retains all attempts");
Check(Reports.Json(run).Contains("\"hadFailures\": true"), "report preserves historical failures");
Check(!Reports.Json(run).Contains(token), "report excludes token");
Check(PackageJson.Import(PackageJson.Write(package)).Cases[0].Id == test.Id, "package round-trip");
await Reject(() => Task.Run(() => PackageJson.Import(PackageJson.Write(package with { SchemaVersion = "9.0" }))), "unsupported version rejected");
await Reject(() => Task.Run(() => PackageJson.Import("{\"schemaVersion\":\"1.0\",\"id\":\"invalid\",\"title\":\"Invalid\",\"cases\":[null]}")), "null case rejected");
await Reject(() => Task.Run(() => PackageJson.Import(PackageJson.Write(package).Replace("\"baseUrl\"", "\"misspelledUrl\""))), "unknown contract field rejected");
await Reports.WriteAtomicAsync(Path.Combine(directory, "report.json"), Reports.Json(run));
Check(File.Exists(Path.Combine(directory, "report.json")), "atomic report export");
await Reject(() => coordinator.StartAsync(package, "https://user:password@example.test"), "credential-bearing base URL rejected before persistence");
var beforeStorageFailure = server.Requests.Count;
await Reject(() => new RunCoordinator(new FailingStore(), new HttpTransport()).ExecuteAsync(run, test, test.Request, [new()], none, default), "journal failure blocks execution");
Check(server.Requests.Count == beforeStorageFailure, "unrecordable attempt never reaches network");
string ReadShared(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var copy = new MemoryStream(); stream.CopyTo(copy); return Encoding.UTF8.GetString(copy.ToArray()); }
Check(!ReadShared(Path.Combine(directory, "runs.db")).Contains(token), "SQLite pages exclude token");
var wal = Path.Combine(directory, "runs.db-wal");
Check(!File.Exists(wal) || !ReadShared(wal).Contains(token), "SQLite write-ahead log excludes token");
await DatabaseTests.RunAsync(Check, Reject, server.Url);
var visibleRun = await coordinator.StartAsync(package, server.Url);
var fullBody = await coordinator.ExecuteAsync(visibleRun, test, test.Request with { Url = "/secret" }, [new()],
    new("none", AuthKind.Bearer, "response-setting-auth-canary"), default, redactResponseBody: false);
Check(fullBody.Response!.Body.Contains("server-canary-681"), "response redaction can be disabled for capture");
Check(!PackageJson.Write(fullBody).Contains("response-setting-auth-canary"), "unredacted response capture still masks authentication headers");
Check((await store.LoadAsync(visibleRun.Id))!.Attempts[0].Response!.Body == fullBody.Response.Body
    && Reports.Json(visibleRun).Contains("server-canary-681"), "full response survives saved run and report export");
var maskedBody = await coordinator.ExecuteAsync(visibleRun, test, test.Request with { Url = "/secret" }, [new()], none, default);
Check(!maskedBody.Response!.Body.Contains("server-canary-681") && fullBody.Response.Body.Contains("server-canary-681"), "redaction resumes for future captures without changing history");
await RunHistoryTests.RunAsync(Check, Reject);
await AuthenticationTests.RunAsync(Check, Reject, server.Url);
await DraftTests.RunAsync(Check);
await ReportTests.RunAsync(Check, directory);
await ManualAssessmentTests.RunAsync(Check, Reject, directory);
var invalidExpectation = new TestPackage { Cases = [new() { Assertions = [new() { Kind = CheckKind.JsonEquals, Expected = "plain-secret-sentinel" }] }] };
try { PackageJson.Import(PackageJson.Write(invalidExpectation)); Check(false, "Invalid JSON expectation must fail"); }
catch (PackageFieldValidationException ex) { Check(ex.FieldPath == "$.cases[0].assertions[0].expected" && !ex.Message.Contains("plain-secret-sentinel"), "invalid expectation identifies its field without exposing supplied values"); }
foreach (var expectedValue in new[] { "\"hello\"", "true", "42", "null" })
{
    invalidExpectation.Cases[0].Assertions[0].Expected = expectedValue;
    Check(PackageJson.Import(PackageJson.Write(invalidExpectation)).Cases.Count == 1, "JSON expectation accepts correctly encoded string, boolean, number or null");
}
await AgentTests.RunAsync(Check, Reject, server);
await AgentPreparationTests.RunAsync(Check, Reject, directory, server);
await PollingTests.RunAsync(Check, server.Url);
await BodyTests.RunAsync(Check, server.Url);
await WorkspaceTests.RunAsync(Check, Reject, server.Url);
await TransportTests.RunAsync(Check, Reject, server.Url);
await DiagnosticsTests.RunAsync(Check, server.Url);
var actionPackage = new TestPackage { SchemaVersion = "1.6", Cases = [new() { Id = "create" }], GuidedActions = [new() { Id = "cleanup", Request = new() { Method = "DELETE", Url = "/items/{{case.create./id}}", Headers = [new() { Name = "X-Secret", Value = "action-secret", Sensitive = true }] } }] };
Check(PackageJson.Import(PackageJson.Write(actionPackage)).GuidedActions?.Count == 1, "Guided Actions survive package import separately from cases");
Check(actionPackage.Cases.Count == 1 && Reports.Create(new TestRun { Package = actionPackage }).SchemaVersion == "1.9", "Guided Actions do not add suite coverage and use report schema 1.9");
Check(!PackageJson.Write(new Redaction([]).Package(actionPackage)).Contains("action-secret"), "Guided Action requests are redacted during export");
await Reject(() => Task.Run(() => PackageJson.Validate(actionPackage with { SchemaVersion = "1.5" })), "Guided Actions require schema 1.6");
await Reject(() => Task.Run(() => PackageJson.Validate(actionPackage with { GuidedActions = [new() { Id = "create" }] })), "Guided Action and case IDs cannot collide");
await Reject(() => Task.Run(() => PackageJson.Validate(actionPackage with { GuidedActions = [new() { Id = "cleanup", Request = new() { Url = "/items/{{case.missing./id}}" } }] })), "Guided Action bindings require a real source case");
await Reject(() => Task.Run(() => PackageJson.Validate(actionPackage with { GuidedActions = [new() { Id = "cleanup", Assertions = [new() { Kind = CheckKind.JsonEquals, Expected = "unquoted" }] }] })), "Guided Action assertions receive runtime validation");
var recoverySource = new TestRun { Attempts = [new() { Id = "created", CaseId = "create", State = AttemptState.Completed, Verdict = Verdict.Pass, Response = new() { StatusCode = 201, Body = "{\"id\":123}" } }, new() { Id = "duplicate", CaseId = "create", State = AttemptState.Completed, Verdict = Verdict.Fail, Response = new() { StatusCode = 409, Body = "{\"error\":\"duplicate\"}" } }] };
var cleanupRequest = new RequestDefinition { Method = "DELETE", Url = "/items/{{case.create./id}}" };
var recoveredRequest = GuidedActionBindings.Resolve(cleanupRequest, recoverySource);
Check(recoveredRequest.Url == "/items/123" && recoveredRequest.CaptureSources["create"] == "created", "cleanup uses original creation response after duplicate failure and records provenance");
await Reject(() => Task.Run(() => ValueBindings.Request(cleanupRequest, null, recoverySource)), "normal test bindings still reject a latest failed source");
recoverySource.Attempts[0].Verdict = Verdict.Fail;
Check(GuidedActionBindings.Resolve(cleanupRequest, recoverySource).Url == "/items/123", "cleanup accepts a created resource even when other assertions failed");
Check(GuidedActionBindings.Resolve(new RequestDefinition { Url = "/items/456" }, null).Url == "/items/456", "manual cleanup target requires no passing source case");
await Reject(() => Task.Run(() => GuidedActionBindings.Resolve(cleanupRequest, recoverySource, new Dictionary<string, string> { ["create"] = "missing" })), "explicit cleanup source cannot silently fall back to another attempt");
var forceCase = new TestCase { Id = "force-business", Request = new() { Url = "/inspect?item={{case.create./id}}" }, Assertions = [new()] };
var forcePackage = new TestPackage { Cases = [new() { Id = "setup", Kind = TestStepKind.SwaggerAuthentication }, new() { Id = "remote" }, new() { Id = "create" }, forceCase], BaseUrl = server.Url };
var forceRun = await coordinator.StartAsync(forcePackage, server.Url);
forceRun.Attempts.AddRange(recoverySource.Attempts);
await Reject(() => coordinator.ExecuteAsync(forceRun, forceCase, forceCase.Request, forceCase.Assertions, none, default), "normal requests retain setup prerequisite checks");
var forcedAttempt = await coordinator.ExecuteAsync(forceRun, forceCase, forceCase.Request, forceCase.Assertions, none, default, bypassPrerequisites: true);
Check(forcedAttempt.Verdict == Verdict.Pass && forcedAttempt.CaptureSources["create"] == "created" && forcedAttempt.Changes.Any(c => c.Path == "execution.prerequisites"), "Run anyway skips failed prior cases and records override with original response provenance");
var manualForce = await coordinator.ExecuteAsync(forceRun, forceCase, forceCase.Request with { Url = "/inspect?item=456" }, [new() { Expected = "418" }], none, default, bypassPrerequisites: true);
Check(manualForce.Verdict == Verdict.Fail, "Run anyway still evaluates assertions normally for manual values");
await Reject(() => coordinator.ExecuteAsync(forceRun, forceCase, forceCase.Request with { Url = "/inspect?item={{case.missing./id}}" }, forceCase.Assertions, none, default, bypassPrerequisites: true), "Run anyway does not invent missing resource values");
Check(Reports.Json(forceRun).Contains("execution.prerequisites"), "Run anyway override is included in exported report evidence");
if (args.Contains("--live-databases")) await LiveDatabaseTests.RunAsync(Check);
await WorkflowTests.RunAsync(Check, Reject, directory);
await PreparationTests.RunAsync(Check, Reject, directory);
Console.WriteLine($"\n{count} checks passed. Evidence: {directory}");

internal sealed class FailingStore : IRunStore
{
    public Task SaveAsync(TestRun run) => throw new IOException("Simulated full disk");
    public Task<TestRun?> LoadLatestAsync() => Task.FromResult<TestRun?>(null);
}

internal sealed record CapturedRequest(string Method, string RawUrl, string Body, string? Authorization, string? ApiKey);
internal sealed class TestServer : IDisposable
{
    private readonly HttpListener listener = new();
    public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
    public string Url { get; }
    public TestServer(int port = 0)
    {
        if (port == 0) { var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        Url = $"http://localhost:{port}"; listener.Prefixes.Add(Url + "/"); listener.Start(); _ = LoopAsync();
    }
    private async Task LoopAsync()
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
            var request = context.Request;
            using var raw = new MemoryStream(); await request.InputStream.CopyToAsync(raw); raw.Position = 0;
            using var reader = new StreamReader(raw, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            Requests.Enqueue(new(request.HttpMethod, request.RawUrl ?? "", body, request.Headers["Authorization"], request.Headers["X-API-Key"]));
            var response = context.Response; response.ContentType = "application/json";
            string text;
            switch (request.Url?.AbsolutePath)
            {
                case "/bytes": text = JsonSerializer.Serialize(new { base64 = Convert.ToBase64String(raw.ToArray()), contentType = request.ContentType }); break;
                case "/binary": response.ContentType = "application/octet-stream"; var data = new byte[] { 0, 255, 128, 13, 10 }; response.ContentLength64 = data.Length; await response.OutputStream.WriteAsync(data); response.Close(); return;
                case "/set-cookie": response.SetCookie(new Cookie("session", "cookie-canary-123", "/")); text = "{}"; break;
                case "/cookie": response.ContentType = "text/plain"; text = request.Headers["Cookie"] ?? "none"; break;
                case "/slow": await Task.Delay(2000); text = "{}"; break;
                case "/denied": response.StatusCode = 403; text = "{\"error\":\"Access denied as expected\"}"; break;
                case "/redirect": response.StatusCode = 302; response.RedirectLocation = "/health"; text = "{}"; break;
                case "/redirect-loop": response.StatusCode = 302; response.RedirectLocation = "/redirect-loop"; text = "{}"; break;
                case "/redirect-external": response.StatusCode = 302; response.RedirectLocation = "https://example.com/"; text = "{}"; break;
                case "/large": text = new string('x', 200); break;
                case "/secret": text = "{\"password\":\"server-canary-681\"}"; break;
                case "/health": text = "{\"status\":\"ok\",\"service\":\"E2E local demo\"}"; break;
                case "/echo": text = body.Length > 0 ? body : "{}"; break;
                default: text = JsonSerializer.Serialize(new { method = request.HttpMethod, url = request.RawUrl, body, authorization = request.Headers["Authorization"], custom = request.Headers["X-Custom"], apiKey = request.Headers["X-API-Key"] }); break;
            }
            var bytes = Encoding.UTF8.GetBytes(text); response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes); response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException) { }
    }
    public void Dispose() { listener.Stop(); listener.Close(); }
}
