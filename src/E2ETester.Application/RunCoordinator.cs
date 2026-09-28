using System.Security.Cryptography;
using System.Text;
using E2ETester.Core;

namespace E2ETester.Application;

public sealed partial class RunCoordinator(IRunStore store, IHttpTransport transport)
{
    private readonly SemaphoreSlim execution = new(1, 1);

    public async Task<TestRun> StartAsync(TestPackage package, string baseUrl)
    {
        PackageJson.Validate(package);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https") || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0)
            throw new InvalidDataException("Base URL must be HTTP(S) without credentials, query parameters or fragments.");
        var run = new TestRun
        {
            Package = PackageJson.Clone(package), BaseUrl = baseUrl,
            PackageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PackageJson.Write(PackageJson.Clone(package)))))
        };
        await store.SaveAsync(run);
        return run;
    }

    public Task SaveAsync(TestRun run) => store.SaveAsync(run);
    public async Task<TestRun?> ResumeAsync(string? id = null)
    {
        if (!await execution.WaitAsync(0)) throw new InvalidOperationException("Wait for the current request before opening a saved run.");
        try
        {
        var run = id is null ? await store.LoadLatestAsync() : store is IRunHistoryStore history ? await history.LoadAsync(id) : throw new NotSupportedException("This store does not support run history.");
        if (run is null) return null;
        foreach (var a in run.Attempts.Where(a => a.State == AttemptState.Running))
        {
            a.State = AttemptState.Interrupted;
            a.FinishedAt = DateTimeOffset.UtcNow;
            a.Verdict = Verdict.Inconclusive;
            a.Response = new ResponseSnapshot { ErrorCode = "interrupted", Error = "The application stopped. Delivery is unknown; the request was not replayed." };
        }
        foreach (var attempt in run.Attempts)
        {
            for (var i = 0; i < attempt.DatabaseEvidence.Count; i++)
                if (attempt.DatabaseEvidence[i].Result.ErrorCode == "running")
                {
                    var interrupted = attempt.DatabaseEvidence[i] with { Result = attempt.DatabaseEvidence[i].Result with { ErrorCode = "interrupted", Error = "Verification was interrupted and was not replayed." } };
                    if (attempt.DatabasePlans.FirstOrDefault(p => p.Check.Id == interrupted.CheckId) is { } plan) interrupted = interrupted with { Assessment = DatabaseQueries.Assess(plan.Check, interrupted) };
                    attempt.DatabaseEvidence[i] = interrupted;
                }
            Recalculate(attempt);
        }
        await store.SaveAsync(run);
        return run;
        }
        finally { execution.Release(); }
    }

    public async Task<Attempt> ExecuteAsync(TestRun run, TestCase baseline, RequestDefinition draft, List<Assertion> assertions, IdentitySession identity, CancellationToken cancellationToken, FixtureSelection? fixture = null, string? executionId = null, bool preparationConfirmed = false, bool bypassPrerequisites = false, bool applicabilityConfirmed = false, IReadOnlyList<string>? applicabilityIssues = null)
    {
        if (!await execution.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("A request is already running.");
        try
        {
            if (executionId is not null && run.Attempts.FirstOrDefault(a => a.ExecutionId == executionId) is { } known) return known;
            if (run.FinishedAt is not null) throw new InvalidOperationException("This run is finished. Start a new run to execute more tests.");
            if (!run.Package.Cases.Any(c => c.Id == baseline.Id)) throw new InvalidOperationException("The case does not belong to this run.");
            if(baseline.AppliesWhen?.Count>0 && !applicabilityConfirmed) throw new InvalidOperationException("Review tester applicability before execution.");
            if(applicabilityIssues?.Count>0 && !bypassPrerequisites) throw new InvalidOperationException("This case does not apply: " + string.Join(" ",applicabilityIssues));
            var setup = run.Package.Cases.FirstOrDefault(c => c.Kind == TestStepKind.SwaggerAuthentication);
            if (!bypassPrerequisites && setup is not null && baseline.Id != setup.Id)
            {
                if (run.Attempts.LastOrDefault(a => a.CaseId == setup.Id)?.Verdict != Verdict.Pass)
                    throw new InvalidOperationException("Complete the Swagger sign-in step first.");
                var validation = run.Package.Cases.SkipWhile(c => c.Id != setup.Id).Skip(1).FirstOrDefault();
                if (validation is not null && baseline.Id != validation.Id && (run.Attempts.LastOrDefault(a => a.CaseId == validation.Id)?.Verdict != Verdict.Pass || run.Attempts.LastOrDefault(a => a.CaseId == validation.Id)?.StartedAt < run.Attempts.Last(a => a.CaseId == setup.Id).FinishedAt))
                    throw new InvalidOperationException("Complete remote-IP validation before running the suite.");
            }
            if (baseline.Preparation is not null && !preparationConfirmed && !PermissionIdentity.Matches(baseline.Preparation, identity)) throw new InvalidOperationException("This case requires explicit preparation in the desktop app before execution. Automated execution of preparation changes is not enabled.");
            if (baseline.Kind == TestStepKind.SwaggerAuthentication)
            {
                if (identity.Alias != draft.IdentityRef)
                    throw new InvalidOperationException($"The sign-in step requires identity '{draft.IdentityRef}', but '{identity.Alias}' is selected.");
                if (identity.Kind != AuthKind.Bearer)
                    throw new InvalidOperationException($"Choose Bearer authentication for identity '{identity.Alias}' to complete the Swagger sign-in step.");
                if (string.IsNullOrWhiteSpace(identity.Secret))
                    throw new InvalidOperationException($"No bearer token is available for identity '{identity.Alias}'. In Swagger, read the authorized token and choose Use token for selected identity.");
                var auth = new Attempt { ExecutionId = executionId, CaseId = baseline.Id, CaseTitle = baseline.Title,
                    IdentityAlias = identity.Alias, AuthKind = identity.Kind,
                    Request = PackageJson.Clone(new RequestDefinition { Url = baseline.Request.Url, IdentityRef = identity.Alias }),
                    Checks = [new("authentication-token", true, Verdict.Pass, "Bearer token available", "Token supplied", "No API request sent. Token availability only; the next remote-IP request validates server access.")] };
                if(applicabilityIssues?.Count>0) auth.Changes.Add(new("execution.applicability",string.Join(" ",applicabilityIssues),"Tester explicitly chose Run anyway; expected results unchanged."));
                if (bypassPrerequisites) auth.Changes.Add(new("execution.prerequisites", "Normal flow requirements", "Tester explicitly chose Run anyway; authentication availability still verified."));
                run.Attempts.Add(auth);
                try { await store.SaveAsync(run); } catch { run.Attempts.Remove(auth); throw; }
                auth.State = AttemptState.Completed; auth.FinishedAt = DateTimeOffset.UtcNow; Recalculate(auth);
                await store.SaveAsync(run); return auth;
            }
            if (baseline.FixtureRequired && fixture is null)
                throw new InvalidOperationException("This case requires a database fixture. Select a fixture or turn off Require fixture before sending.");
            if (baseline.FixtureQueryId.Length > 0 && fixture is not null)
            {
                var query = run.Package.Queries.Single(q => q.Id == baseline.FixtureQueryId);
                if (fixture is null || fixture.Evidence.Query.Id != query.Id || fixture.Evidence.Query.Sql != query.Sql || fixture.Evidence.Query.ConnectionRef != query.ConnectionRef || fixture.Evidence.Query.Provider != query.Provider)
                    throw new InvalidOperationException("Load and select a fixture from the case's saved query before sending.");
                _ = DatabaseQueries.SelectFixture(fixture.Evidence, fixture.RowIndex);
            }
            fixture = fixture is null ? null : PackageJson.Clone(fixture);
            var frozen = ValueBindings.Request(draft, fixture, run, bypassPrerequisites ? GuidedActionBindings.Defaults(draft, run) : null);
            var resolvedBaseline = bypassPrerequisites || fixture is null && ValueBindings.RequiresFixture(baseline.Request)
                ? PackageJson.Clone(baseline.Request) : ValueBindings.Request(baseline.Request, fixture, run);
            var frozenAssertions = PackageJson.Clone(assertions);
            PackageJson.Validate(new TestPackage { SchemaVersion = run.Package.SchemaVersion, Queries = run.Package.Queries, Cases = [new TestCase { Request = frozen, Assertions = frozenAssertions, FixtureQueryId = baseline.FixtureQueryId, FixtureRequired = baseline.FixtureRequired, DatabaseChecks = baseline.DatabaseChecks }] });
            if (identity.Alias != frozen.IdentityRef) throw new InvalidOperationException("Select the identity required by this request.");
            if (identity.Kind != AuthKind.None && string.IsNullOrWhiteSpace(identity.Secret)) throw new InvalidOperationException($"Identity '{identity.Alias}' has no token. Choose the identity you signed in with in Auth, or sign in for this identity.");
            if (identity.Kind != AuthKind.None && frozen.Headers.Any(f => f.Enabled && f.Name.Equals(identity.Kind == AuthKind.Bearer ? "Authorization" : identity.HeaderName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The identity and request both set the authentication header. Disable the manual header or select no authentication.");
            var url = RequestUrls.Resolve(run.BaseUrl, frozen);
            var safeRequest = PackageJson.Clone(frozen);
            var plans = baseline.DatabaseChecks.Select(check =>
            {
                var query = PackageJson.Clone(run.Package.Queries.Single(q => q.Id == check.QueryId));
                var safeCheck = PackageJson.Clone(check);
                return new DatabaseCheckPlan(safeCheck, query);
            }).ToList();
            var attempt = new Attempt
            {
                ExecutionId = executionId,
                CaptureSources = frozen.CaptureSources,
                CaseId = baseline.Id, CaseTitle = baseline.Title,
                IdentityAlias = identity.Alias, AuthKind = identity.Kind, VerifiedIdentity = identity.Verified, EffectiveUrl = url.AbsoluteUri, Request = safeRequest,
                Changes = Differences(PackageJson.Clone(resolvedBaseline), safeRequest),
                Assertions = frozenAssertions,
                Fixture = fixture, DatabasePlans = plans
            };
            if(applicabilityIssues?.Count>0) attempt.Changes.Add(new("execution.applicability",string.Join(" ",applicabilityIssues),"Tester explicitly chose Run anyway; expected results unchanged."));
            if (bypassPrerequisites) attempt.Changes.Add(new("execution.prerequisites", "Normal flow requirements", "Tester explicitly chose Run anyway; prior-case prerequisites bypassed. Available response source IDs are recorded in captureSources."));
            run.Attempts.Add(attempt);
            try { await store.SaveAsync(run); }
            catch { run.Attempts.Remove(attempt); throw; }
            frozen.SessionScope = run.Id;
            var response = await transport.SendAsync(frozen, url, identity, cancellationToken);
            attempt.Response = PackageJson.Clone(response);
            attempt.Checks = Assertions.Evaluate(frozenAssertions, response);
            attempt.State = response.ErrorCode == "cancelled" ? AttemptState.Cancelled : AttemptState.Completed;
            Recalculate(attempt);
            attempt.FinishedAt = DateTimeOffset.UtcNow;
            await store.SaveAsync(run);
            return attempt;
        }
        finally { execution.Release(); }
    }

    public static List<FieldChange> Differences(RequestDefinition original, RequestDefinition current)
    {
        var changes = new List<FieldChange>();
        void Add(string path, string a, string b) { if (a != b) changes.Add(new(path, a, b)); }
        Add("method", original.Method, current.Method); Add("url", original.Url, current.Url);
        Add("body", original.Body, current.Body); Add("sendBody", original.SendBody.ToString(), current.SendBody.ToString());
        Add("contentType", original.ContentType, current.ContentType); Add("identity", original.IdentityRef, current.IdentityRef);
        Add("timeoutSeconds", original.TimeoutSeconds.ToString(), current.TimeoutSeconds.ToString());
        Add("bodyKind", original.BodyKind.ToString(), current.BodyKind.ToString());
        Add("parts", PackageJson.Write(original.Parts), PackageJson.Write(current.Parts));
        Add("useCookies", original.UseCookies.ToString(), current.UseCookies.ToString());
        Add("httpVersion", original.HttpVersion, current.HttpVersion); Add("proxyUrl", original.ProxyUrl, current.ProxyUrl); Add("clientCertificateThumbprint", original.ClientCertificateThumbprint, current.ClientCertificateThumbprint);
        Add("followRedirects", original.FollowRedirects.ToString(), current.FollowRedirects.ToString()); Add("maxRedirects", original.MaxRedirects.ToString(), current.MaxRedirects.ToString());
        foreach (var (label, left, right) in new[] { ("headers", original.Headers, current.Headers), ("query", original.Query, current.Query) })
        {
            foreach (var f in right)
            {
                var before = left.FirstOrDefault(x => x.Id == f.Id);
                if (before is null) changes.Add(new($"{label}.{f.Name}", "<absent>", f.Value));
                else { Add($"{label}.{f.Id}.name", before.Name, f.Name); Add($"{label}.{f.Id}.value", before.Value, f.Value); Add($"{label}.{f.Id}.enabled", before.Enabled.ToString(), f.Enabled.ToString()); }
            }
            foreach (var f in left.Where(f => right.All(r => r.Id != f.Id))) changes.Add(new($"{label}.{f.Name}", f.Value, "<removed>"));
        }
        return changes;
    }
}
