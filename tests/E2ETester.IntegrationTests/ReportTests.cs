using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;

internal static class ReportTests
{
    public static async Task RunAsync(Action<bool, string> check, string artifactDirectory)
    {
        var run = new TestRun
        {
            Id = "mixed-report", BaseUrl = "https://example.test", PackageHash = "snapshot-hash",
            Package = new TestPackage { Title = "Mixed run <script>alert('title')</script>", Cases = [new() { Id = "rerun", Title = "Failed then passed" }, new() { Id = "failed", Title = "Failure" }, new() { Id = "unknown", Title = "Interrupted" }, new() { Id = "untested", Title = "Not executed" }, new() { Id = "variation", Title = "Custom variation", ParentId = "rerun" }] }
        };
        Attempt Result(string id, Verdict verdict) => new()
        {
            CaseId = id, CaseTitle = run.Package.Cases.Single(c => c.Id == id).Title, State = AttemptState.Completed, Verdict = verdict,
            EffectiveUrl = "https://example.test/items", Request = new() { Url = "/items" },
            Assertions = [new() { Id = "json-check", Kind = CheckKind.JsonEquals, Expected = "42" }],
            Checks = [new("json-check", true, verdict, "42", verdict == Verdict.Pass ? "42" : "41", "Compare typed values")],
            Response = new() { StatusCode = 200, Body = "<script>alert('response')</script><img src=x onerror=alert(1)>", DurationMs = 15 }
        };
        run.Attempts.Add(Result("rerun", Verdict.Fail)); run.Attempts.Add(Result("rerun", Verdict.Pass));
        run.Attempts.Add(Result("failed", Verdict.Fail));
        run.Attempts.Add(new Attempt { CaseId = "unknown", CaseTitle = "Interrupted", State = AttemptState.Interrupted, Response = new() { ErrorCode = "interrupted", Error = "Delivery unknown", Truncated = true } });
        run.Attempts.Add(Result("variation", Verdict.Pass));
        run.Attempts[4].DatabaseEvidence.Add(new QueryEvidence { Query = new() { Title = "Stored database evidence" }, Result = new() { Truncated = true }, Assessment = new("db:old", true, Verdict.Fail, "1", "0", "Historical failure") });
        var exported = DateTimeOffset.UtcNow;
        var report = Reports.Create(run, exported);
        check(report.Summary.TotalCases == 5 && report.Summary.ExecutedCases == 4 && report.Summary.Attempts == 5 && report.Summary.Verdict == Verdict.Fail, "report mixed-run counts and effective verdict agree");
        check(report.Coverage.Single(c => c.CaseId == "rerun").Verdict == Verdict.Pass && report.Summary.HadFailures, "report preserves historical failure despite passing rerun");
        check(report.Coverage.Single(c => c.CaseId == "untested") is { State: "NotRun", Verdict: null, AttemptId: null }, "report distinguishes missing coverage from an executed inconclusive test");
        check(report.CheckEvidence[0].Expected!.Value.ValueKind == JsonValueKind.Number && report.CheckEvidence[0].Actual!.Value.GetInt32() == 41, "report checks retain typed JSON values");
        using (var document = JsonDocument.Parse(PackageJson.Write(report)))
        {
            foreach (var item in report.CheckEvidence)
            {
                var parts = item.EvidencePointer.Split('/');
                var evidence = document.RootElement.GetProperty("run").GetProperty("attempts")[int.Parse(parts[3])].GetProperty("checks")[int.Parse(parts[5])];
                if (evidence.GetProperty("id").GetString() != item.CheckId) throw new Exception("Report evidence pointer does not resolve.");
            }
            check(true, "every report check pointer resolves to recorded evidence");
        }
        var html = Reports.Html(report);
        var readable = Reports.Text(run);
        check(readable.Contains("TC-1 · Failed then passed · PASS") && readable.Contains("TC-2 · Failure · FAIL") && readable.Contains("TC-3 · Interrupted · INCONCLUSIVE") && readable.Contains("TC-4 · Not executed · NOT RUN") && readable.Contains("TC-5 · Custom variation · PASS"), "readable summary numbers every case and uses its latest automated outcome");
        check(readable.IndexOf("TEST CASE SUMMARY", StringComparison.Ordinal) < readable.IndexOf("DETAILED RESULTS", StringComparison.Ordinal) && html.Contains("Test case summary") && html.Contains("<strong>TC-5</strong>"), "readable and HTML reports lead with numbered test case summaries");
        check(html.Contains("PROVISIONAL") && html.Contains("Historical failures") && html.Contains("NOT RUN") && html.Contains("INCONCLUSIVE"), "HTML exposes provisional, historical and incomplete outcomes");
        check(!html.Contains("<script>") && !html.Contains("<img") && html.Contains("&lt;script&gt;") && html.Contains("default-src 'none'"), "HTML escapes hostile evidence and blocks active content");
        check(html.Contains("Incomplete database evidence") && html.Contains("Response truncated") && html.Contains("Custom variation"), "HTML includes variations and incomplete HTTP/database evidence");
        check(PackageJson.Write(report) == PackageJson.Write(Reports.Create(run, exported)) && html == Reports.Html(report), "report output is deterministic with declared export timestamp");
        run.Package.Title = "Later edit"; run.Attempts.Clear();
        check(report.Run.Attempts.Count == 5 && report.Run.Package.Title != run.Package.Title, "export snapshot is detached from subsequent run edits");
        report.Run.FinishedAt = exported;
        var finished = Reports.Create(report.Run, exported);
        check(!finished.Provisional && Reports.Html(finished).Contains("FINISHED RUN"), "finished report is distinguished from checkpoint export");
        await Reports.WriteAtomicAsync(Path.Combine(artifactDirectory, "mixed-report.json"), PackageJson.Write(finished));
        await Reports.WriteAtomicAsync(Path.Combine(artifactDirectory, "mixed-report.html"), Reports.Html(finished));
        check(Directory.GetFiles(artifactDirectory, "*.tmp").Length == 0, "report export leaves no temporary artifacts");
    }
}
