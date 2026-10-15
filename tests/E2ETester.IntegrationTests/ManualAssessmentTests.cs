using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class ManualAssessmentTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string directory)
    {
        using var store = new SqliteRunStore(Path.Combine(directory, "assessments"));
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var test = new TestCase { Id = "reviewed", Title = "Review business behavior" };
        var run = await coordinator.StartAsync(new TestPackage { Cases = [test] }, "https://example.test");
        var attempt = new Attempt
        {
            CaseId = test.Id, CaseTitle = test.Title, State = AttemptState.Completed, Verdict = Verdict.Pass,
            Checks = [new("status", true, Verdict.Pass, "200", "200", "HTTP matches")], Response = new() { StatusCode = 200, Body = "{}" }
        };
        run.Attempts.Add(attempt); await store.SaveAsync(run);
        var responseBefore = PackageJson.Write(attempt.Response);
        var fail = await coordinator.AssessAsync(run, attempt, "Reviewer A", Verdict.Fail, "The displayed value is incorrect.");
        check(attempt.Verdict == Verdict.Pass && Outcomes.ForRun(run) == Verdict.Fail && fail.AutomatedVerdict == Verdict.Pass, "manual FAIL is recorded separately from automated PASS");
        check(fail.ReviewerSource == "self_declared" && ManualAssessments.IsCurrent(attempt, fail), "assessment records reviewer provenance and current evidence fingerprint");
        var pass = await coordinator.AssessAsync(run, attempt, "Reviewer B", Verdict.Pass, "Confirmed the expected value. <script>alert(1)</script> token-canary", ["token-canary"]);
        check(attempt.ManualAssessments.Count == 2 && attempt.ManualAssessments[0].Id == fail.Id && attempt.ManualAssessments[1].Id == pass.Id, "reassessment appends without overwriting earlier review");
        var report = Reports.Create(run);
        check(report.Summary.HadFailures && report.Coverage[0].ManualVerdict == Verdict.Pass && report.Coverage[0].ManualAssessmentIsCurrent == true && report.Coverage[0].Verdict == Verdict.Pass, "report distinguishes latest manual review and historical manual failures");
        check(Reports.Json(run).Contains("token-canary") && !Reports.Html(run).Contains("<script>"), "manual notes are preserved and HTML-encoded");
        await coordinator.AttachDatabaseEvidenceAsync(run, attempt, new QueryEvidence());
        check(!ManualAssessments.IsCurrent(attempt, pass) && Reports.Create(run).Coverage[0].ManualAssessmentIsCurrent == false, "new database evidence makes prior manual assessment stale");
        check(Reports.Html(run).Contains("Evidence changed — review again"), "HTML explains stale manual assessments");
        await coordinator.AssessAsync(run, attempt, "Reviewer C", Verdict.Inconclusive, "Waiting for a business requirement clarification.");
        check(Reports.Create(run).Coverage[0].ManualVerdict == Verdict.Inconclusive && Reports.Create(run).Coverage[0].ManualAssessmentIsCurrent == true, "a new review can assess the updated evidence as inconclusive");
        check(PackageJson.Write(attempt.Response) == responseBefore, "assessments never rewrite raw HTTP response evidence");
        await reject(() => coordinator.AssessAsync(run, attempt, " ", Verdict.Pass, "Reason"), "reviewer is required");
        await reject(() => coordinator.AssessAsync(run, attempt, "Reviewer", Verdict.Pass, " "), "assessment reason is required");
        await reject(() => coordinator.AssessAsync(run, attempt, "Reviewer", (Verdict)999, "Reason"), "invalid manual verdict rejected");
        await reject(() => coordinator.AssessAsync(run, attempt, "Reviewer", Verdict.Pass, new string('x', 4001)), "manual reason size bounded");
        await reject(() => coordinator.AssessAsync(run, new Attempt(), "Reviewer", Verdict.Pass, "Reason"), "foreign attempt cannot receive an assessment");
        var running = new Attempt { CaseId = test.Id }; run.Attempts.Add(running);
        await reject(() => coordinator.AssessAsync(run, running, "Reviewer", Verdict.Pass, "Reason"), "running attempt cannot be assessed"); run.Attempts.Remove(running);
        var before = attempt.ManualAssessments.Count;
        await reject(() => new RunCoordinator(new FailingStore(), new HttpTransport()).AssessAsync(run, attempt, "Reviewer", Verdict.Fail, "Unsaved review"), "storage failure is surfaced when recording assessment");
        check(attempt.ManualAssessments.Count == before, "failed assessment persistence rolls back the in-memory append");
        var restored = (await coordinator.ResumeAsync(run.Id))!;
        check(restored.Attempts[0].ManualAssessments.Count == 3 && ManualAssessments.IsCurrent(restored.Attempts[0], restored.Attempts[0].ManualAssessments[^1]), "assessment history and evidence fingerprint survive reload");
        run.FinishedAt = DateTimeOffset.UtcNow; await coordinator.SaveAsync(run);
        await reject(() => coordinator.AssessAsync(run, attempt, "Reviewer", Verdict.Pass, "Reason"), "finished run assessment history is immutable");
        await CheckManualFlow(check, reject, directory);
        var json = Reports.Json(run);
        await Reports.WriteAtomicAsync(Path.Combine(directory, "manual-report.json"), json);
        await Reports.WriteAtomicAsync(Path.Combine(directory, "manual-report.html"), Reports.Html(run));
    }
    private static async Task CheckManualFlow(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string directory)
    {
        using var store = new SqliteRunStore(Path.Combine(directory, "manual-flow"));
        var coordinator = new RunCoordinator(store, new PassingTransport());
        var source = new TestCase { Id = "create", Title = "Create item" };
        var next = new TestCase { Id = "read", Title = "Read item", Request = new() { Url = "/items/{{case.create./id}}" }, Assertions = [new() { Kind = CheckKind.Status, Expected = "200" }] };
        var run = await coordinator.StartAsync(new TestPackage { Cases = [source, next] }, "https://example.test");
        var attempt = new Attempt { CaseId = source.Id, State = AttemptState.Completed, Verdict = Verdict.Inconclusive,
            Response = new() { StatusCode = 201, Body = "{\"id\":42}" }, Checks = [new("db", true, Verdict.Inconclusive, "confirmed", "pending", "Database check not run")] };
        run.Attempts.Add(attempt);
        var legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PackageJson.Write(new {
            attempt.Id, attempt.CaseId, attempt.State, attempt.Verdict, attempt.IdentityAlias, attempt.AuthKind,
            attempt.VerifiedIdentity, attempt.EffectiveUrl, attempt.Request, attempt.Assertions, attempt.Response,
            attempt.Checks, attempt.Changes, attempt.Fixture, attempt.DatabasePlans, attempt.DatabaseEvidence, attempt.CaptureSources
        }))));
        check(ManualAssessments.EvidenceHash(attempt) == legacyHash, "existing journal assessment hashes remain compatible");
        Task<Attempt> Send() => coordinator.ExecuteAsync(run, next, next.Request, next.Assertions, new(next.Request.IdentityRef, AuthKind.None), default);
        await reject(() => Send(), "unreviewed inconclusive response blocks normal dependent send");
        await coordinator.AssessAsync(run, attempt, "Tester", Verdict.Pass, "Verified the created item manually.");
        check(ValueBindings.Request(next.Request, null, run).Url == "/items/42", "manual PASS unlocks typed response binding without Run anyway");
        var child = await Send();
        check(child.Verdict == Verdict.Pass && !child.Changes.Any(c => c.Path == "execution.prerequisites"), "dependent case passes normally after manual PASS");
        check(attempt.Verdict == Verdict.Inconclusive && Outcomes.ForRun(run) == Verdict.Pass && Reports.Create(run).Summary.Verdict == Verdict.Pass, "effective run result honors manual PASS while retaining automated inconclusive");
        check(Reports.SimpleText(run).Contains("TC-1 · Create item · PASS") && Reports.Text(run).Contains("TC-1 · Create item · PASS"), "human report summaries use effective manual result");
        var resumed = (await coordinator.ResumeAsync(run.Id))!;
        check(ValueBindings.Request(next.Request, null, resumed).Url == "/items/42" && Outcomes.ForRun(resumed) == Verdict.Pass, "manual PASS remains effective after save and resume");
        await coordinator.AssessAsync(run, attempt, "Tester", Verdict.Fail, "Correction: data is wrong.");
        await reject(() => Send(), "latest manual FAIL supersedes manual PASS for follow-up");
        check(Outcomes.ForRun(run) == Verdict.Fail, "manual FAIL determines effective run outcome");
        await coordinator.AssessAsync(run, attempt, "Tester", Verdict.Pass, "Correction verified.");
        attempt.Response = attempt.Response with { Body = "{\"id\":43}" };
        await reject(() => Send(), "changed response does not reuse a stale manual PASS");
        check(Outcomes.ForAttempt(attempt) == Verdict.Inconclusive, "stale manual verdict falls back to automated verdict");
        await coordinator.AssessAsync(run, attempt, "Tester", Verdict.Pass, "New response verified.");
        run.Attempts.Add(new Attempt { CaseId = source.Id, State = AttemptState.Completed, Verdict = Verdict.Fail, Response = new() { StatusCode = 409, Body = "{}" } });
        await reject(() => Send(), "manual PASS on an older attempt does not approve a new failed attempt");
        var suite = SuiteDefaults.Create();
        suite.Cases[1] = suite.Cases[1] with { Assertions = [] };
        suite.Cases.Add(new TestCase { Id = "business", Request = new() { Url = "/business", IdentityRef = "cloud-user" }, Assertions = [new() { Kind = CheckKind.Status, Expected = "200" }] });
        var guided = await coordinator.StartAsync(suite, "https://example.test");
        Task<Attempt> GuidedSend(int index) => coordinator.ExecuteAsync(guided, suite.Cases[index], suite.Cases[index].Request, suite.Cases[index].Assertions, new("cloud-user", AuthKind.Bearer, "local-test-token"), default);
        await GuidedSend(0);
        var validation = await GuidedSend(1);
        check(validation.Verdict == Verdict.Inconclusive, "validation without checks starts inconclusive");
        await reject(() => GuidedSend(2), "unreviewed gateway validation blocks business request");
        await coordinator.AssessAsync(guided, validation, "Tester", Verdict.Pass, "Gateway response verified manually.");
        check((await GuidedSend(2)).Verdict == Verdict.Pass, "manual PASS satisfies gateway prerequisite without Run anyway");
    }
    private sealed class PassingTransport : IHttpTransport
    {
        public Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken)
            => Task.FromResult(new ResponseSnapshot { StatusCode = 200, Body = "{}" });
    }

}
