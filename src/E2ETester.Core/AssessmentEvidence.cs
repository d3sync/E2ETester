using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace E2ETester.Core;

public static class AssessmentEvidence
{
    // Keep the original fingerprint serialization stable for saved assessments.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string EvidenceHash(Attempt attempt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
    {
        attempt.Id, attempt.CaseId, attempt.State, attempt.Verdict, attempt.IdentityAlias, attempt.AuthKind,
        attempt.VerifiedIdentity, attempt.EffectiveUrl, attempt.Request, attempt.Assertions, attempt.Response,
        attempt.Checks, attempt.Changes, attempt.Fixture, attempt.DatabasePlans, attempt.DatabaseEvidence, attempt.CaptureSources
    }, Options))));
    public static bool IsCurrent(Attempt attempt, ManualAssessment assessment) => assessment.EvidenceHash == EvidenceHash(attempt);
}
