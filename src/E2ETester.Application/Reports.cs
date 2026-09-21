using E2ETester.Core;

namespace E2ETester.Application;

public static partial class Reports
{
    public static string Json(TestRun run) => PackageJson.Write(Create(run));
    public static TestReport Create(TestRun source, DateTimeOffset? exportedAt = null)
    {
        // Snapshot the already-redacted journal; rendering must never mutate a live run.
        var run = PackageJson.Clone(source);
        var checks = new List<ReportCheck>();
        for (var ai = 0; ai < run.Attempts.Count; ai++)
        {
            var attempt = run.Attempts[ai];
            for (var ci = 0; ci < attempt.Checks.Count; ci++)
            {
                var check = attempt.Checks[ci];
                var assertion = attempt.Assertions.FirstOrDefault(a => a.Id == check.Id);
                var structured = assertion?.Kind is CheckKind.JsonEquals or CheckKind.Status or CheckKind.StatusIn or CheckKind.JsonExists or CheckKind.JsonGreaterThan or CheckKind.JsonLessThan or CheckKind.JsonArrayCount or CheckKind.DurationLessThan || attempt.DatabasePlans.Any(p => "db:" + p.Check.Id == check.Id);
                checks.Add(new(attempt.Id, check.Id, check.Required, check.Verdict, Value(check.Expected, structured), Value(check.Actual, structured), check.Expected, check.Actual, check.Message, $"/run/attempts/{ai}/checks/{ci}"));
            }
        }
        return new("1.7", run.Id, exportedAt ?? DateTimeOffset.UtcNow, run.FinishedAt is null, "0.7.7", null,
            new(Outcomes.ForRun(run), run.Package.Cases.Count, run.Package.Cases.Count(c => run.Attempts.Any(a => a.CaseId == c.Id)), run.Attempts.Count,
                run.Attempts.Any(a => a.Verdict == Verdict.Fail || a.DatabaseEvidence.Any(e => e.Assessment?.Verdict == Verdict.Fail) || a.ManualAssessments.Any(m => m.Verdict == Verdict.Fail)), "latest attempt per case; all historical attempts retained"),
            run.Package.Cases.Select(c => { var latest = run.Attempts.LastOrDefault(a => a.CaseId == c.Id); var assessment = latest?.ManualAssessments.LastOrDefault(); return new CaseCoverage(c.Id, c.Title, c.ParentId, latest?.Id, latest?.Verdict, latest?.State.ToString() ?? "NotRun", assessment?.Verdict, assessment?.Id, assessment is null ? null : ManualAssessments.IsCurrent(latest!, assessment)); }).ToList(),
            checks, "Credentials, sensitive fields and known credential echoes are omitted. This is not a replay bundle.", run);
    }
    private static System.Text.Json.JsonElement? Value(string value, bool structured)
    {
        if (!structured) return System.Text.Json.JsonSerializer.SerializeToElement(value);
        try { return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public static async Task WriteAtomicAsync(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
