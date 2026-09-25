using System.Text.Json;
using System.Text.RegularExpressions;
using E2ETester.Core;

namespace E2ETester.Application;

public interface IPreparationTarget
{
    Task<string> ReadAsync(PreparationPlan plan, CancellationToken token);
    Task ReplaceAsync(PreparationPlan plan, string expectedJson, string replacementJson, CancellationToken token);
}

public static class Preparations
{
    public static PreparationAudit Audit(PreparationRecovery entry, IEnumerable<string>? secrets = null)
    {
        var plan = PackageJson.Clone(entry.Plan);
        plan.UpdateRequest.Body = plan.UpdateRequest.Body.Replace("{{valueJson}}", plan.ValueJson);
        plan.CaptureRequest = PackageJson.Clone(plan.CaptureRequest);
        plan.UpdateRequest = PackageJson.Clone(plan.UpdateRequest);
        return new(DateTimeOffset.UtcNow, entry.CaseId, plan.Title, entry.Status)
        {
            BaseUrl = entry.BaseUrl, OperationId = entry.Id, RunId = entry.RunId, IdentityRef = entry.IdentityRef,
            ExecutionJson = PackageJson.Write(plan),
            HttpEvidenceJson = PackageJson.Write(entry.HttpEvidence)
        };
    }
    public static void Validate(PreparationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Title)) throw new InvalidDataException("Preparation needs a title.");
        Scalar(plan.ValueJson); Scalar(plan.KeyJson);
        if (!Enum.IsDefined(plan.Kind) || !Enum.IsDefined(plan.Provider)) throw new InvalidDataException("Unknown preparation type or provider.");
        if (plan.Flags.Any(f => string.IsNullOrWhiteSpace(f.Name) || f.Value < 0)) throw new InvalidDataException("Flags need names and nonnegative 64-bit values.");
        if (plan.Kind == PreparationKind.DatabaseValue)
        {
            if (string.IsNullOrWhiteSpace(plan.ConnectionRef)) throw new InvalidDataException("Preparation needs a local connection alias.");
            foreach (var identifier in new[] { plan.Table, plan.KeyColumn, plan.ValueColumn })
                if (!Regex.IsMatch(identifier, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")) throw new InvalidDataException("Use simple database identifiers; table names may include a schema.");
            if (plan.KeyColumn.Contains('.') || plan.ValueColumn.Contains('.')) throw new InvalidDataException("Column names must not contain a schema.");
            if (Scalar(plan.KeyJson).ValueKind == JsonValueKind.Null) throw new InvalidDataException("A target key is required.");
        }
        else
        {
            PackageJson.ValidateRequest(plan.CaptureRequest, "Preparation capture");
            PackageJson.ValidateRequest(plan.UpdateRequest, "Preparation update");
            if (plan.CaptureRequest.Method != "GET" || plan.CaptureRequest.FollowRedirects || plan.UpdateRequest.FollowRedirects)
                throw new InvalidDataException("Capture must use GET and preparation requests must not follow redirects.");
            if (plan.UpdateRequest.Method is not ("PUT" or "PATCH" or "POST") || !plan.UpdateRequest.SendBody || plan.UpdateRequest.BodyKind != RequestBodyKind.Text || !plan.UpdateRequest.Body.Contains("{{valueJson}}"))
                throw new InvalidDataException("API preparation needs a PUT/PATCH/POST JSON body containing {{valueJson}} for apply and restore.");
            using var body = JsonDocument.Parse(plan.UpdateRequest.Body.Replace("{{valueJson}}", plan.ValueJson));
            if (!plan.ValuePointer.StartsWith('/')) throw new InvalidDataException("Capture needs a JSON pointer such as /permissions.");
        }
    }
    public static JsonElement Scalar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array) throw new InvalidDataException("Preparation values must be JSON scalars.");
        return doc.RootElement.Clone();
    }
    public static bool Equal(string left, string right) => JsonElement.DeepEquals(Scalar(left), Scalar(right));

    // Persist before a write: an interrupted or uncertain change remains restorable.
    public static async Task ApplyAsync(PreparationRecovery entry, IPreparationTarget target, Func<Task> persist, CancellationToken token)
    {
        Validate(entry.Plan); await persist();
        await target.ReplaceAsync(entry.Plan, entry.OriginalJson, entry.Plan.ValueJson, token);
        entry.Status = "applied"; await persist();
    }
    public static async Task RestoreAsync(PreparationRecovery entry, IPreparationTarget target, Func<Task> persist, CancellationToken token)
    {
        if (entry.Status == "restored") return;
        var current = await target.ReadAsync(entry.Plan, token);
        if (!Equal(current, entry.OriginalJson))
        {
            if (!Equal(current, entry.Plan.ValueJson)) throw new InvalidOperationException("The target changed outside this preparation. Resolve the conflict before restoring; no value was overwritten.");
            await target.ReplaceAsync(entry.Plan, current, entry.OriginalJson, token);
        }
        var previousStatus = entry.Status;
        entry.Status = "restored";
        try { await persist(); }
        catch { entry.Status = previousStatus; throw; }
    }
}
