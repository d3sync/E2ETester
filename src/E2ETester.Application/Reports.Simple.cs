using System.Net;
using System.Text;
using E2ETester.Core;

namespace E2ETester.Application;
public static partial class Reports
{
    public static string FileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var safe = new string(Path.GetFileNameWithoutExtension(name).Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 100) safe = safe[..100];
        return string.IsNullOrWhiteSpace(safe) ? "test-package" : safe;
    }
    public static string SimpleText(TestRun run)
    {
        var report = Create(run); var text = new StringBuilder();
        string Preview(string value) => value.Length <= 8192 ? value : value[..8192] + "\n[Preview shortened. Complete data is in the full ZIP.]";
        text.AppendLine(run.Package.Title).AppendLine($"Result: {report.Summary.Verdict.ToString().ToUpperInvariant()} · {(run.FinishedAt is null ? "IN PROGRESS" : "FINISHED")}")
            .AppendLine($"Run: {run.Id} · Started: {run.StartedAt:u}").AppendLine("Latest attempt per case; earlier attempts and full evidence are in the ZIP.");
        for (var i = 0; i < report.Coverage.Count; i++)
        {
            var c = report.Coverage[i];
            text.AppendLine($"TC-{i + 1} · {c.Title} · {Effective(c)?.ToString().ToUpperInvariant() ?? "NOT RUN"}" +
                (c.ManualVerdict is {} v ? $" · MANUAL {v.ToString().ToUpperInvariant()}{(c.ManualAssessmentIsCurrent == false ? " (review again)" : "")}" : ""));
        }
        foreach (var c in run.Package.Cases)
        {
            var attempt = run.Attempts.LastOrDefault(a => a.CaseId == c.Id);
            text.AppendLine().AppendLine(c.Title);
            if (attempt is null) { text.AppendLine("NOT RUN"); continue; }
            text.AppendLine($"Automated: {attempt.Verdict.ToString().ToUpperInvariant()} · Identity: {attempt.IdentityAlias} · {attempt.StartedAt:u}");
            if (c.Kind == TestStepKind.SwaggerAuthentication) text.AppendLine("Sign-in recorded; no HTTP request.");
            else
            {
                text.AppendLine($"IN: {attempt.Request.Method} {attempt.EffectiveUrl}");
                foreach (var field in attempt.Request.Query.Where(f => f.Enabled)) text.AppendLine($"Parameter {field.Name}: {Preview(field.Value)}");
                if (attempt.Request.SendBody) text.AppendLine(Preview(attempt.Request.Body));
                foreach (var part in attempt.Request.Parts.Where(p => p.Enabled)) text.AppendLine($"{part.Name}: {Preview(part.Value)}");
                text.AppendLine($"OUT: HTTP {attempt.Response?.StatusCode} · {attempt.Response?.DurationMs} ms");
                text.AppendLine(Preview(attempt.Response?.Body ?? "No response captured."));
                if (attempt.Response?.Truncated == true) text.AppendLine("Response capture was truncated.");
                if (attempt.Response?.Error is {} error) text.AppendLine(error);
            }
            foreach (var check in attempt.Checks.Where(c => c.Verdict != Verdict.Pass)) text.AppendLine($"{check.Verdict}: {check.Message}");
            foreach (var change in attempt.Changes.Where(c => c.Path.StartsWith("execution."))) text.AppendLine(change.Original + " · " + change.Current);
            foreach (var review in attempt.ManualAssessments) text.AppendLine($"Manual {review.Verdict}: {review.Reason}");
        }
        if (run.PreparationLog is { Count: > 0 }) text.AppendLine().AppendLine("Preparation / restoration: " + string.Join(", ", run.PreparationLog.Select(p => p.Status)));
        return text.ToString();
    }
    public static string SimpleHtml(TestRun run) => "<!doctype html><html><head><meta charset=\"utf-8\"><title>Test report</title><style>body{font:16px system-ui;max-width:1100px;margin:40px auto;padding:20px;color:#1f2328;background:#fff}pre{font:inherit;white-space:pre-wrap;overflow-wrap:anywhere}</style></head><body><pre>" + WebUtility.HtmlEncode(SimpleText(run)) + "</pre></body></html>";
    public static async Task<(string Simple, string Full)> WritePairAsync(string directory, string packageFileName, TestRun run, DateTimeOffset? timestamp = null)
    {
        Directory.CreateDirectory(directory);
        var name = FileStem(packageFileName) + "_report" + (timestamp ?? DateTimeOffset.Now).ToString("yyyyMMdd_HHmmss_fff");
        var simple = Path.Combine(directory, name + ".html"); var full = Path.Combine(directory, name + "_full.zip");
        // Never silently overwrite a report from another run with the same package and timestamp.
        for (var suffix = 2; File.Exists(simple) || File.Exists(full); suffix++)
        { simple = Path.Combine(directory, name + "_" + suffix + ".html"); full = Path.Combine(directory, name + "_" + suffix + "_full.zip"); }
        await WriteBundleAsync(full, run);
        await WriteAtomicAsync(simple, SimpleHtml(run));
        return (simple, full);
    }
}
