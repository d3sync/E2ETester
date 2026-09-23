using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;

public sealed record AgentIdentity(string Alias, AuthKind Kind, string SecretEnvironmentVariable, string HeaderName = "Authorization", string Prefix = "", BrowserLoginSettings? Browser = null);
public sealed record AgentDatabase(string Alias, DatabaseProvider Provider, string ConnectionEnvironmentVariable, bool AllowPreparation = false);
public sealed record AgentConfiguration
{
    public string Workspace { get; init; } = "";
    public List<string> AllowedBaseUrls { get; init; } = [];
    public bool AllowHttp { get; init; }
    public bool AllowAuthoring { get; init; }
    public bool AllowSavedQueries { get; init; }
    public bool AllowInteractiveLogin { get; init; }
    public bool AllowPreparation { get; init; }
    public List<PreparationPlan> ApprovedPreparations { get; init; } = [];
    public List<QueryDefinition> SavedQueries { get; init; } = [];
    public List<AgentIdentity> Identities { get; init; } = [];
    public List<AgentDatabase> Databases { get; init; } = [];
}
public sealed record PreparedExecution(string Id, string RunId, string CaseId, string Revision, TestCase Case, FixtureSelection? Fixture, string State = "prepared");

public sealed partial class AgentWorkspace : IAsyncDisposable
{
    private readonly AgentConfiguration configuration;
    private readonly SqliteRunStore store;
    private readonly RunCoordinator coordinator;
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource Cancel)> active = new();
    private readonly ConcurrentDictionary<string, BrowserTokenSession> browserSessions = new();
    private static string? EnvironmentValue(string name) => string.IsNullOrWhiteSpace(name) ? null : Environment.GetEnvironmentVariable(name);
    public AgentWorkspace(AgentConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Workspace)) throw new InvalidDataException("A workspace path is required.");
        foreach (var value in configuration.AllowedBaseUrls)
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidDataException("Allowed targets must be HTTP(S) base URLs without credentials.");
        this.configuration = configuration; store = new(configuration.Workspace); coordinator = new(store, new HttpTransport());
    }
    public object Capabilities() => new { configuration.AllowHttp, configuration.AllowAuthoring, configuration.AllowSavedQueries, configuration.AllowInteractiveLogin, preparation = configuration.AllowPreparation && OperatingSystem.IsWindows(), configuration.AllowedBaseUrls, queries = new Redaction(Secrets()).Package(new TestPackage { Queries = configuration.SavedQueries }).Queries, identities = configuration.Identities.Select(i => new { i.Alias, i.Kind, browser = i.Browser is not null, available = i.Browser is not null ? browserSessions.TryGetValue(i.Alias, out var session) && !session.RequiresLogin : !string.IsNullOrEmpty(EnvironmentValue(i.SecretEnvironmentVariable)) }), authentication = "Interactive login uses the user's browser only when host configuration allows it. No tool accepts or reveals credentials." };
    private void Idle() { if (active.Values.Any(v => !v.Task.IsCompleted)) throw new InvalidOperationException("operation_running: inspect or cancel the active operation first."); }
    private void Target(Uri uri)
    {
        // Reject ambiguous escaped path separators/dot segments before proxy/server decoding.
        var path = uri.AbsolutePath;
        if (uri.UserInfo.Length > 0 || System.Text.RegularExpressions.Regex.IsMatch(path, "%(2f|5c|2e|25)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new InvalidOperationException("target_not_permitted");
        if (!configuration.AllowedBaseUrls.Any(b => { var root = new Uri(b.TrimEnd('/') + "/"); return uri.Scheme == root.Scheme && uri.Host == root.Host && uri.Port == root.Port && (uri.AbsolutePath == root.AbsolutePath.TrimEnd('/') || uri.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)); })) throw new InvalidOperationException("target_not_permitted");
    }
    private async Task<IdentitySession> Identity(string alias)
    {
        if (alias == "none") return new("none", AuthKind.None);
        var profile = configuration.Identities.SingleOrDefault(i => i.Alias == alias) ?? throw new InvalidOperationException("authentication_required: configure the identity outside MCP.");
        if (profile.Browser is not null)
        {
            if (!browserSessions.TryGetValue(alias, out var session)) throw new InvalidOperationException("authentication_required");
            try { return await session.GetIdentityAsync(alias, true, default); }
            catch { browserSessions.TryRemove(alias, out _); throw new InvalidOperationException("authentication_required"); }
        }
        var secret = EnvironmentValue(profile.SecretEnvironmentVariable);
        if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("authentication_required: configure the identity outside MCP.");
        return new(alias, profile.Kind, secret, profile.HeaderName, profile.Prefix);
    }
    private IEnumerable<string> Secrets() => configuration.Identities.Select(i => EnvironmentValue(i.SecretEnvironmentVariable) ?? "").Concat(configuration.Databases.Select(d => EnvironmentValue(d.ConnectionEnvironmentVariable) ?? ""));
    private static string Revision(TestRun run) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PackageJson.Write(new { run.Package, run.PreparationLog, attempts = run.Attempts.Select(a => new { a.Id, a.State, a.Verdict }) }))));
    public Task<IReadOnlyList<RunSummary>> ListRunsAsync(int offset = 0) => store.ListAsync(offset);
    public async Task<TestRun> GetRunAsync(string id) => await store.LoadAsync(id) ?? throw new InvalidDataException("run_not_found");
    private Task SavePreparation(PreparedExecution p) => store.SaveOperationAsync(p.Id, PackageJson.Write(p));
    private async Task<PreparedExecution> Preparation(string id) => PackageJson.Read<PreparedExecution>(await store.LoadOperationAsync(id) ?? throw new InvalidDataException("execution_not_found"));
    public async Task<TestRun> StartAsync(string packageJson, string baseUrl)
    {
        await commands.WaitAsync();
        try { Idle(); if (!configuration.AllowHttp) throw new InvalidOperationException("http_not_permitted"); Target(new Uri(baseUrl)); var package = PackageJson.Import(packageJson); return await coordinator.StartAsync(new Redaction(Secrets()).Package(package), baseUrl); }
        finally { commands.Release(); }
    }
    public async Task<object> PrepareAsync(string runId, string caseId, string? draftJson = null, string? fixtureEvidenceId = null, int rowIndex = 0)
    {
        await commands.WaitAsync();
        try
        {
            Idle(); if (!configuration.AllowHttp) throw new InvalidOperationException("http_not_permitted");
            var run = await coordinator.ResumeAsync(runId) ?? throw new InvalidDataException("run_not_found");
            if (run.FinishedAt is not null) throw new InvalidOperationException("run_finished");
            var test = PackageJson.Clone(run.Package.Cases.Single(c => c.Id == caseId));
            if (draftJson is not null) { if (!configuration.AllowAuthoring) throw new InvalidOperationException("authoring_not_permitted"); test = test with { Request = PackageJson.Read<RequestDefinition>(draftJson) }; }
            FixtureSelection? fixture = null;
            if (fixtureEvidenceId is not null) fixture = DatabaseQueries.SelectFixture(PackageJson.Read<QueryEvidence>(await store.LoadOperationAsync("query:" + fixtureEvidenceId) ?? throw new InvalidDataException("fixture_not_found")), rowIndex);
            var request = ValueBindings.Request(test.Request, fixture, run); Target(RequestUrls.Resolve(run.BaseUrl, request));
            if (request.FollowRedirects || request.ProxyUrl.Length > 0 || request.ClientCertificateThumbprint.Length > 0 || request.Headers.Any(h => h.Enabled && h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("target_not_permitted: MCP requires explicit cases, default proxy and configured token identities.");
            _ = await Identity(request.IdentityRef);
            var safe = new Redaction(Secrets().Concat(Redaction.Secrets(request))).Package(new TestPackage { Cases = [test], Queries = run.Package.Queries }).Cases[0];
            var p = new PreparedExecution(Guid.NewGuid().ToString("N"), runId, caseId, Revision(run), safe, fixture);
            await SavePreparation(p);
            return new { executionId = p.Id, revision = p.Revision, preview = safe with { Request = new Redaction(Secrets().Concat(Redaction.Secrets(request))).Request(request) }, fixture = fixture?.Evidence.Id, note = "Call execute_prepared with this ID. Repeating the ID never sends another request." };
        }
        finally { commands.Release(); }
    }
    public async Task<object> ExecuteAsync(string executionId)
    {
        await commands.WaitAsync();
        try
        {
            var p = await Preparation(executionId);
            if (p.State != "prepared") return await OperationAsync(executionId);
            Idle(); if (!configuration.AllowHttp) throw new InvalidOperationException("http_not_permitted");
            var run = await coordinator.ResumeAsync(p.RunId) ?? throw new InvalidDataException("run_not_found");
            if (run.FinishedAt is not null) throw new InvalidOperationException("run_finished");
            if (p.Revision != Revision(run)) throw new InvalidOperationException("stale_preparation: prepare again after editing the package.");
            Target(RequestUrls.Resolve(run.BaseUrl, ValueBindings.Request(p.Case.Request, p.Fixture, run)));
            var identity = await Identity(p.Case.Request.IdentityRef);
            await EnsureEnvironmentAsync(run, p.Case, identity);
            await SavePreparation(p with { State = "dispatched" });
            var cancel = new CancellationTokenSource();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await coordinator.ExecuteAsync(run, run.Package.Cases.Single(c => c.Id == p.CaseId), p.Case.Request, p.Case.Assertions, identity, cancel.Token, p.Fixture, p.Id, preparationConfirmed: true);
                    await SavePreparation(p with { State = "finished" });
                }
                catch { try { await SavePreparation(p with { State = "failed_or_interrupted" }); } catch { /* Durable dispatch prevents retry even when storage fails. */ } }
            });
            active[p.Id] = (task, cancel); start.SetResult();
            return new { executionId, state = "running" };
        }
        finally { commands.Release(); }
    }
    public async Task<object> OperationAsync(string id)
    {
        if (id.StartsWith("login:", StringComparison.Ordinal))
        {
            var document = await store.LoadOperationAsync(id) ?? throw new InvalidDataException("execution_not_found");
            var state = PackageJson.Read<System.Text.Json.JsonElement>(document);
            if (state.GetProperty("state").GetString() == "running" && !active.ContainsKey(id)) return new { executionId = id, state = "interrupted", note = "Login was interrupted. Request a new browser login." };
            return state;
        }
        var p = await Preparation(id); var run = await GetRunAsync(p.RunId);
        var attempt = run.Attempts.FirstOrDefault(a => a.ExecutionId == id);
        return new { executionId = id, state = p.State == "dispatched" ? (active.TryGetValue(id, out var a) && !a.Task.IsCompleted ? "running" : "interrupted") : p.State, attemptId = attempt?.Id, verdict = attempt?.Verdict, note = "An interrupted operation is never replayed. Inspect the run before preparing an intentional new attempt." };
    }
    public object Cancel(string id) { if (active.TryGetValue(id, out var op)) op.Cancel.Cancel(); return new { executionId = id, cancellationRequested = active.ContainsKey(id) }; }
    public async Task<object> AuthenticateAsync(string alias)
    {
        await commands.WaitAsync();
        try
        {
            Idle(); if (!configuration.AllowInteractiveLogin) throw new InvalidOperationException("authentication_required");
            var settings = configuration.Identities.SingleOrDefault(i => i.Alias == alias)?.Browser ?? throw new InvalidOperationException("authentication_required");
            BrowserTokenSession.Validate(settings); browserSessions.TryRemove(alias, out _);
            var id = "login:" + Guid.NewGuid().ToString("N"); var cancel = new CancellationTokenSource();
            await store.SaveOperationAsync(id, PackageJson.Write(new { executionId = id, state = "running", alias }));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var session = await BrowserTokenSession.LoginAsync(settings, cancellationToken: cancel.Token); browserSessions[alias] = session;
                    await store.SaveOperationAsync(id, PackageJson.Write(new { executionId = id, state = "finished", alias, identity = session.Identity }));
                }
                catch { try { await store.SaveOperationAsync(id, PackageJson.Write(new { executionId = id, state = "authentication_required", alias })); } catch { } }
            });
            active[id] = (task, cancel); start.SetResult(); return new { executionId = id, state = "running", action = "Complete sign-in in your browser; inspect get_operation. Never provide passwords or OTPs to an agent." };
        }
        finally { commands.Release(); }
    }
    public Task<IReadOnlyList<LibraryEntry>> ListPackagesAsync() => store.ListPackagesAsync();
    public Task<TestPackage?> GetPackageAsync(string id) => store.LoadPackageAsync(id);
    public async Task SavePackageAsync(string json)
    {
        await commands.WaitAsync();
        try { Idle(); if (!configuration.AllowAuthoring) throw new InvalidOperationException("authoring_not_permitted"); await store.SavePackageAsync(PackageJson.Import(json), Secrets()); }
        finally { commands.Release(); }
    }
    public async Task<TestCase> VariationAsync(string runId, string caseId, string requestJson, string title)
    {
        await commands.WaitAsync();
        try
        {
            Idle(); if (!configuration.AllowAuthoring) throw new InvalidOperationException("authoring_not_permitted");
            var run = await GetRunAsync(runId); if (run.FinishedAt is not null) throw new InvalidOperationException("run_finished");
            var parent = run.Package.Cases.Single(c => c.Id == caseId);
            var variation = parent with { Id = Guid.NewGuid().ToString("N"), ParentId = parent.Id, Title = title, Request = PackageJson.Read<RequestDefinition>(requestJson) };
            variation = new Redaction(Secrets().Concat(Redaction.Secrets(variation.Request))).Package(new TestPackage { Cases = [variation], Queries = run.Package.Queries }).Cases[0];
            run.Package.Cases.Insert(run.Package.Cases.IndexOf(parent) + 1, variation); PackageJson.Validate(run.Package); await coordinator.SaveAsync(run); return variation;
        }
        finally { commands.Release(); }
    }
    private IReadOnlyList<DatabaseConnectionProfile> Databases() => configuration.Databases.Select(d => new DatabaseConnectionProfile(d.Alias, d.Provider, EnvironmentValue(d.ConnectionEnvironmentVariable) ?? "", true)).ToList();
    private void QueryAllowed(QueryDefinition query) { if (!configuration.AllowSavedQueries || !configuration.SavedQueries.Any(q => q.Id == query.Id && DatabaseQueries.Fingerprint(q) == DatabaseQueries.Fingerprint(query))) throw new InvalidOperationException("query_not_permitted"); }
    public async Task<QueryEvidence> QueryAsync(string runId, string queryId, CancellationToken token = default)
    {
        await commands.WaitAsync();
        try
        {
            Idle(); var run = await GetRunAsync(runId); var query = run.Package.Queries.Single(q => q.Id == queryId); QueryAllowed(query);
            var result = await new DatabaseQueries(new DatabaseExecutor()).ExecuteAsync(query, Databases().Single(d => d.Alias == query.ConnectionRef), token);
            await store.SaveOperationAsync("query:" + result.Id, PackageJson.Write(result)); return result;
        }
        finally { commands.Release(); }
    }
    public async Task VerifyAsync(string runId, string attemptId, CancellationToken token)
    {
        await commands.WaitAsync(token);
        try { Idle(); var run = await GetRunAsync(runId); var attempt = run.Attempts.Single(a => a.Id == attemptId); foreach (var plan in attempt.DatabasePlans) QueryAllowed(plan.Query); await coordinator.VerifyDatabaseAsync(run, attempt, new DatabaseQueries(new DatabaseExecutor()), Databases(), token); }
        finally { commands.Release(); }
    }
    public async Task FinishAsync(string runId)
    {
        await commands.WaitAsync();
        try { Idle(); var run = await GetRunAsync(runId); await RestoreEnvironmentCoreAsync(run, default); run.FinishedAt ??= DateTimeOffset.UtcNow; await coordinator.SaveAsync(run); }
        finally { commands.Release(); }
    }
    public async Task AssessAsync(string runId, string attemptId, string reviewer, Verdict verdict, string reason)
    {
        await commands.WaitAsync();
        try { Idle(); var run = await GetRunAsync(runId); await coordinator.AssessAsync(run, run.Attempts.Single(a => a.Id == attemptId), reviewer, verdict, reason, Secrets()); }
        finally { commands.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var item in active.Values) item.Cancel.Cancel();
        await Task.WhenAll(active.Values.Select(a => a.Task));
        foreach (var item in active.Values) item.Cancel.Dispose();
        store.Dispose(); commands.Dispose();
    }
}
