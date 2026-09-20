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
        check(attempt.Verdict == Verdict.Pass && Outcomes.ForRun(run) == Verdict.Pass && fail.AutomatedVerdict == Verdict.Pass, "manual FAIL is recorded separately from automated PASS");
        check(fail.ReviewerSource == "self_declared" && ManualAssessments.IsCurrent(attempt, fail), "assessment records reviewer provenance and current evidence fingerprint");
        var pass = await coordinator.AssessAsync(run, attempt, "Reviewer B", Verdict.Pass, "Confirmed the expected value. <script>alert(1)</script> token-canary", ["token-canary"]);
        check(attempt.ManualAssessments.Count == 2 && attempt.ManualAssessments[0].Id == fail.Id && attempt.ManualAssessments[1].Id == pass.Id, "reassessment appends without overwriting earlier review");
        var report = Reports.Create(run);
        check(report.Summary.HadFailures && report.Coverage[0].ManualVerdict == Verdict.Pass && report.Coverage[0].ManualAssessmentIsCurrent == true && report.Coverage[0].Verdict == Verdict.Pass, "report distinguishes latest manual review and historical manual failures");
        check(!Reports.Json(run).Contains("token-canary") && !Reports.Html(run).Contains("<script>"), "manual notes are redacted and HTML-encoded");
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
        var json = Reports.Json(run);
        await Reports.WriteAtomicAsync(Path.Combine(directory, "manual-report.json"), json);
        await Reports.WriteAtomicAsync(Path.Combine(directory, "manual-report.html"), Reports.Html(run));
    }
}
