using E2ETester.Core;
namespace E2ETester.Application;

public static class GuidedActionBindings
{
    public static IEnumerable<Attempt> Candidates(TestRun? run, string caseId) => (run?.Attempts ?? [])
        .Where(a => a.CaseId == caseId && a is { State: AttemptState.Completed, Response: { Truncated: false, ErrorCode: null, BodyEncoding: "text" } }).Reverse();
    // A duplicate create failure must not hide the earlier response that actually created the resource.
    public static Dictionary<string, string> Defaults(RequestDefinition request, TestRun? run) => ValueBindings.Dependencies(request).Distinct()
        .Select(id => (Id: id, Attempt: Candidates(run, id).FirstOrDefault(a => a.Response!.StatusCode is >= 200 and < 300) ?? Candidates(run, id).FirstOrDefault()))
        .Where(x => x.Attempt is not null).ToDictionary(x => x.Id, x => x.Attempt!.Id);
    public static RequestDefinition Resolve(RequestDefinition request, TestRun? run, IReadOnlyDictionary<string, string>? selected = null)
        => ValueBindings.Request(request, null, run, selected ?? Defaults(request, run));
}
