using E2ETester.Core;

namespace E2ETester.Application;

public static class ManualAssessments
{
    public static string EvidenceHash(Attempt attempt) => AssessmentEvidence.EvidenceHash(attempt);
    public static bool IsCurrent(Attempt attempt, ManualAssessment assessment) => AssessmentEvidence.IsCurrent(attempt, assessment);
}

public sealed partial class RunCoordinator
{
    public async Task<ManualAssessment> AssessAsync(TestRun run, Attempt attempt, string reviewer, Verdict verdict, string reason, IEnumerable<string>? sessionSecrets = null)
    {
        if (!await execution.WaitAsync(0)) throw new InvalidOperationException("Wait for the current operation before assessing an attempt.");
        try
        {
            EnsureCanAttach(run, attempt);
            if (!Enum.IsDefined(verdict)) throw new InvalidDataException("Choose PASS, FAIL or INCONCLUSIVE.");
            reviewer = reviewer.Trim(); reason = reason.Trim();
            if (reviewer.Length is < 1 or > 120 || reason.Length is < 1 or > 4000) throw new InvalidDataException("Enter a reviewer (1–120 characters) and a reason (1–4,000 characters).");
            var assessment = new ManualAssessment(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, reviewer, verdict, reason, attempt.Verdict, ManualAssessments.EvidenceHash(attempt));
            attempt.ManualAssessments.Add(assessment);
            try { await store.SaveAsync(run); }
            catch { attempt.ManualAssessments.Remove(assessment); throw; }
            return assessment;
        }
        finally { execution.Release(); }
    }
}
