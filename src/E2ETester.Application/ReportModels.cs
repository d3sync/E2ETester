using System.Text.Json;
using E2ETester.Core;

namespace E2ETester.Application;

public sealed record TestReport(string SchemaVersion, string ReportId, DateTimeOffset ExportedAt, bool Provisional,
    string ApplicationVersion, string? TargetBuild, ReportSummary Summary, List<CaseCoverage> Coverage,
    List<ReportCheck> CheckEvidence, string Redactions, TestRun Run);
public sealed record ReportSummary(Verdict Verdict, int TotalCases, int ExecutedCases, int Attempts, bool HadFailures, string EffectiveAttemptPolicy);
public sealed record CaseCoverage(string CaseId, string Title, string? ParentId, string? AttemptId, Verdict? Verdict, string State,
    Verdict? ManualVerdict, string? ManualAssessmentId, bool? ManualAssessmentIsCurrent);
public sealed record ReportCheck(string AttemptId, string CheckId, bool Required, Verdict Verdict, JsonElement? Expected,
    JsonElement? Actual, string ExpectedText, string ActualText, string Message, string EvidencePointer);
