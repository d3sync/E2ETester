using E2ETester.Core;
namespace E2ETester.Application;

public static class GuidedActionBindings
{
    public static IEnumerable<Attempt> Candidates(TestRun? run, string caseId, RequestDefinition? request = null) => (run?.Attempts ?? [])
        .Where(a => a.CaseId == caseId && a is { State: AttemptState.Completed, Response: { Truncated: false, ErrorCode: null, BodyEncoding: "text" } } && (request is null || SuppliesValues(request, run!, a))).Reverse();
    // A duplicate create failure must not hide the earlier response that actually created the resource.
    public static Dictionary<string, string> Defaults(RequestDefinition request, TestRun? run) => ValueBindings.Dependencies(request).Distinct()
        .Select(id => (Id: id, Attempt: Candidates(run, id, request).FirstOrDefault(a => a.Response!.StatusCode is >= 200 and < 300) ?? Candidates(run, id, request).FirstOrDefault()))
        .Where(x => x.Attempt is not null).ToDictionary(x => x.Id, x => x.Attempt!.Id);
    private static bool SuppliesValues(RequestDefinition request, TestRun run, Attempt attempt)
    {
        try
        {
            var selected = new Dictionary<string, string> { [attempt.CaseId] = attempt.Id };
            foreach (var expression in ValueBindings.CaseExpressions(request, attempt.CaseId))
                _ = ValueBindings.Resolve(expression, null, run: run, selectedAttempts: selected);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException or System.Text.Json.JsonException) { return false; }
    }
    public static RequestDefinition Resolve(RequestDefinition request, TestRun? run, IReadOnlyDictionary<string, string>? selected = null)
    {
        selected ??= Defaults(request, run);
        foreach (var id in ValueBindings.Dependencies(request))
            if (!selected.ContainsKey(id)) throw new InvalidOperationException($"No saved response containing the required resource values was found for '{id}'. Use Find saved resource, choose another run, or enter the verified target manually.");
        return ValueBindings.Request(request, null, run, selected);
    }
}
