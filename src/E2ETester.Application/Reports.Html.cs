using System.Net;
using System.Text;
using E2ETester.Core;

namespace E2ETester.Application;

public static partial class Reports
{
    public static string Html(TestRun run) => Html(Create(run));
    public static string Html(TestReport report)
    {
        var output = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>E2E test report</title><style>");
        output.Append("body{font:15px/1.6 system-ui,sans-serif;color:#18343c;background:#f4f7f7;margin:0}main{max-width:1100px;margin:auto;padding:32px}h1{font-size:32px;line-height:1.2}h2{margin-top:32px}h3{margin:0}header,article,.card{background:white;border:1px solid #d8e4e5;border-radius:12px;padding:24px;margin:16px 0}.muted{color:#526b72}.badge{display:inline-block;border-radius:5px;padding:2px 9px;font-weight:700;background:#e8edef}.pass{background:#dcf4e9;color:#12563b}.fail{background:#ffe1e0;color:#8c2820}.inconclusive{background:#fff0cf;color:#754e10}table{width:100%;border-collapse:collapse;table-layout:fixed}td,th{padding:9px;text-align:left;vertical-align:top;border-bottom:1px solid #e3eaea;overflow-wrap:anywhere}th{font-size:12px;text-transform:uppercase}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:12px/1.6 ui-monospace,Consolas,monospace;background:#f4f7f7;padding:14px;border-radius:6px;max-height:440px;overflow:auto}summary{cursor:pointer;font-weight:600;padding:9px 0}.notice{border-left:4px solid #d89d2b;padding:10px 16px;background:#fff5dd}.metrics{display:flex;gap:24px;flex-wrap:wrap}a{color:#126a60}small{color:#526b72}@media print{body{background:white}main{padding:0}pre{max-height:none;overflow:visible}article{break-inside:avoid}details:not([open])::after{content:'Expand this section in the HTML file to inspect the evidence.'}}@media(max-width:650px){main{padding:12px}header,article,.card{padding:14px}}");
        output.Append("</style></head><body><main><header><small>E2E TESTER · REPORT 1.7</small><h1>").Append(E(report.Run.Package.Title)).Append("</h1><small>Automated outcome: </small>");
        Badge(report.Summary.Verdict);
        output.Append(" <strong>").Append(report.Provisional ? "PROVISIONAL · run still open" : "FINISHED RUN").Append("</strong><p class=\"muted\">").Append(E(report.Run.BaseUrl)).Append("</p><div class=\"metrics\">");
        output.Append($"<span><strong>{report.Summary.ExecutedCases}/{report.Summary.TotalCases}</strong> cases tested</span><span><strong>{report.Summary.Attempts}</strong> attempts</span><span><strong>{report.Summary.TotalCases - report.Summary.ExecutedCases}</strong> untested</span></div>");
        output.Append("<p><small>Run ").Append(E(report.ReportId)).Append(" · Started ").Append(E(report.Run.StartedAt.ToString("u"))).Append(" · Exported ").Append(E(report.ExportedAt.ToString("u"))).Append("</small></p></header>");
        if (report.Summary.HadFailures) output.Append("<p class=\"notice\">Historical failures are present. A later passing attempt does not erase earlier failed evidence.</p>");
        output.Append("<p class=\"muted\">").Append(E(report.Redactions)).Append(" Latest attempt per case determines automated coverage. Manual assessments do not override it. Missing tests remain incomplete.</p><h2>Test case summary</h2><div class=\"card\"><table><thead><tr><th>Case</th><th>Automated</th><th>Lifecycle</th><th>Manual assessment</th></tr></thead><tbody>");
        var caseNumber = 0;
        foreach (var c in report.Coverage)
        {
            output.Append("<tr><td><strong>TC-").Append(++caseNumber).Append("</strong> · ").Append(E(c.Title));
            if (c.ParentId is not null) output.Append(" <small>· variation</small>");
            output.Append("<br><small>").Append(E(c.CaseId)).Append("</small></td><td>");
            if (c.Verdict is { } verdict) Badge(verdict); else output.Append("NOT RUN");
            output.Append("</td><td>").Append(E(c.State)).Append("</td><td>");
            if (c.ManualVerdict is { } manual) { Badge(manual); output.Append(c.ManualAssessmentIsCurrent == true ? "<br><small>Evidence unchanged</small>" : "<br><small>Evidence changed — review again</small>"); }
            else output.Append("Not assessed");
            output.Append("</td></tr>");
        }
        output.Append("</tbody></table></div><h2>All attempts</h2>");
        if (report.Run.Attempts.Count == 0) output.Append("<p>No requests were executed.</p>");
        for (var i = 0; i < report.Run.Attempts.Count; i++)
        {
            var attempt = report.Run.Attempts[i];
            output.Append("<article><h3>").Append(i + 1).Append(". ").Append(E(attempt.CaseTitle)).Append("</h3>"); Badge(attempt.Verdict);
            output.Append(" <small>").Append(E(attempt.State.ToString())).Append(" · ").Append(E(attempt.StartedAt.ToString("u"))).Append(" · Identity: ").Append(E(attempt.IdentityAlias)).Append("</small><p>").Append(E(attempt.Request.Method)).Append(" ").Append(E(attempt.EffectiveUrl)).Append("</p>");
            if (attempt.VerifiedIdentity is { } identity) output.Append("<p class=\"muted\">Verified subject: ").Append(E(identity.Subject)).Append(" · Issuer: ").Append(E(identity.Issuer)).Append(" · Source: ").Append(E(identity.Source)).Append("</p>");
            if (attempt.Response is { } response)
            {
                output.Append("<p>Status: ").Append(response.StatusCode?.ToString() ?? "No response").Append(" · ").Append(response.DurationMs).Append(" ms · ").Append(response.CapturedBytes).Append(" captured bytes</p>");
                if (response.Truncated) output.Append("<p class=\"notice\">Response truncated: captured evidence is incomplete.</p>");
                if (response.ErrorCode is not null) output.Append("<p class=\"notice\">").Append(E(response.ErrorCode)).Append(": ").Append(E(response.Error)).Append("</p>");
            }
            output.Append("<table><thead><tr><th>Check</th><th>Expected</th><th>Actual</th><th>Result</th></tr></thead><tbody>");
            foreach (var check in attempt.Checks)
            {
                output.Append("<tr><td>").Append(E(check.Id)).Append(check.Required ? "<br><small>Required</small>" : "<br><small>Optional</small>").Append("<br>").Append(E(check.Message)).Append("</td><td>").Append(E(check.Expected)).Append("</td><td>").Append(E(check.Actual)).Append("</td><td>"); Badge(check.Verdict); output.Append("</td></tr>");
            }
            output.Append("</tbody></table>");
            if (attempt.Checks.Count == 0) output.Append("<p class=\"notice\">No checks were recorded for this attempt.</p>");
            Detail("Request and parameters", PackageJson.Write(attempt.Request));
            Detail("Prepared request / cURL", RequestDiagnostics.Describe(attempt));
            Detail("Changes from original", PackageJson.Write(attempt.Changes));
            if (attempt.Response is not null) { Detail("Response body (" + attempt.Response.BodyEncoding + ")", attempt.Response.Body); Detail("Transport and redirects", PackageJson.Write(new { attempt.Response.HttpVersion, attempt.Response.Redirects, attempt.CaptureSources })); Detail("Response headers", PackageJson.Write(attempt.Response.Headers)); }
            if (attempt.Fixture is not null) Detail("Selected database fixture", PackageJson.Write(attempt.Fixture));
            foreach (var evidence in attempt.DatabaseEvidence)
            {
                output.Append("<h4>Database: ").Append(E(evidence.Query.Title)).Append(" · ").Append(E(evidence.Purpose)).Append("</h4>");
                if (evidence.Assessment is { } assessment) Badge(assessment.Verdict);
                if (evidence.Result.Truncated || evidence.Result.ErrorCode is not null) output.Append("<p class=\"notice\">Incomplete database evidence: ").Append(E(evidence.Result.ErrorCode ?? "truncated")).Append("</p>");
                Detail("Query, parameters and captured rows", PackageJson.Write(evidence));
            }
            foreach (var assessment in attempt.ManualAssessments)
            {
                output.Append("<h4>Manual assessment</h4>"); Badge(assessment.Verdict);
                output.Append(" <strong>").Append(E(assessment.Reviewer)).Append("</strong> <small>· Self-declared reviewer · ").Append(E(assessment.CreatedAt.ToString("u"))).Append("</small><p>").Append(E(assessment.Reason)).Append("</p><small>Automated result when reviewed: ").Append(E(assessment.AutomatedVerdict.ToString())).Append(" · ").Append(ManualAssessments.IsCurrent(attempt, assessment) ? "Evidence unchanged" : "Evidence changed — review again").Append("<br>Assessment ID: ").Append(E(assessment.Id)).Append("</small>");
            }
            output.Append("<p><small>Attempt ID: ").Append(E(attempt.Id)).Append("</small></p></article>");
        }
        Detail("Package definition and provenance", PackageJson.Write(new { report.Run.PackageHash, report.Run.Package, report.ApplicationVersion, report.TargetBuild }));
        output.Append("<footer class=\"muted\">Self-contained report. No scripts, network requests or external assets. Expand evidence sections before printing.</footer></main></body></html>");
        return output.ToString();
        void Badge(Verdict verdict) => output.Append("<span class=\"badge ").Append(verdict.ToString().ToLowerInvariant()).Append("\">").Append(verdict.ToString().ToUpperInvariant()).Append("</span>");
        void Detail(string title, string text) => output.Append("<details><summary>").Append(E(title)).Append("</summary><pre>").Append(E(text)).Append("</pre></details>");
    }
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
