using System.Text;
using E2ETester.Core;
namespace E2ETester.Application;
public static partial class Reports
{
    public static string Text(TestRun run)
    {
        var report = Create(run); var text = new StringBuilder();
        text.AppendLine(run.Package.Title).AppendLine($"Result: {report.Summary.Verdict} · Run {run.Id}").AppendLine(run.FinishedAt is null ? "IN PROGRESS" : "FINISHED");
        text.AppendLine().AppendLine("TEST CASE SUMMARY");
        for (var i = 0; i < report.Coverage.Count; i++)
        {
            var test = report.Coverage[i];
            var title = string.Join(" ", test.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            text.AppendLine($"TC-{i + 1} · {title} · {test.Verdict?.ToString().ToUpperInvariant() ?? "NOT RUN"}");
        }
        if (report.Coverage.Count == 0) text.AppendLine("No test cases.");
        text.AppendLine("Latest automated result per test case. Manual assessments are shown separately below.");
        if (report.Summary.HadFailures) text.AppendLine("Historical failures are retained in the detailed results.");
        text.AppendLine().AppendLine(report.Redactions);
        if (run.PreparationLog is { Count: > 0 })
        {
            text.AppendLine().AppendLine("PREPARATION AND RESTORATION");
            foreach (var entry in run.PreparationLog) text.AppendLine($"{entry.At:u} · {entry.CaseId} · {entry.Title} · {entry.Status}");
        }
        text.AppendLine().AppendLine("DETAILED RESULTS");
        foreach (var test in run.Package.Cases)
        {
            text.AppendLine().AppendLine(test.Title);
            var attempts = run.Attempts.Where(a => a.CaseId == test.Id).ToArray();
            if (attempts.Length == 0) text.AppendLine("NOT RUN");
            foreach (var attempt in attempts)
            {
                text.AppendLine($"{attempt.StartedAt:u} · {attempt.Verdict} · {attempt.IdentityAlias}");
                foreach (var check in attempt.Checks) text.AppendLine($"  {check.Verdict}: {check.Message} | expected {check.Expected} | actual {check.Actual}");
                if (attempt.Response is not null) text.AppendLine(RequestDiagnostics.Describe(attempt)).AppendLine("Response:").AppendLine(attempt.Response.Body);
                else text.AppendLine("No HTTP response (authentication-only or interrupted step).");
                if (attempt.DatabaseEvidence.Count > 0) text.AppendLine("Database evidence:").AppendLine(PackageJson.Write(attempt.DatabaseEvidence));
                foreach (var assessment in attempt.ManualAssessments) text.AppendLine($"Manual assessment: {assessment.Verdict} · {assessment.Reviewer} · {assessment.Reason}");
            }
        }
        return text.ToString();
    }
}
