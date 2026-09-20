using E2ETester.Core;

namespace E2ETester.Application;

public sealed record WorkspaceDraft
{
    public int Version { get; init; } = 1;
    public string? RunId { get; init; }
    public string? SelectedCaseId { get; init; }
    public TestPackage Baseline { get; init; } = new();
    public TestPackage Edited { get; init; } = new();
    public QueryDefinition? QueryDraft { get; init; }

    public WorkspaceDraft Redacted(IEnumerable<string> sessionSecrets)
    {
        var packages = new[] { Baseline, Edited };
        var secrets = packages.SelectMany(p => p.Cases).SelectMany(c => Redaction.Secrets(c.Request))
            .Concat(packages.SelectMany(p => p.Queries).SelectMany(q => q.Parameters).Where(p => p.Sensitive || Redaction.IsSensitive(p.Name)).Select(p => p.Value))
            .Concat(QueryDraft?.Parameters.Where(p => p.Sensitive || Redaction.IsSensitive(p.Name)).Select(p => p.Value) ?? []).Concat(sessionSecrets);
        var redactor = new Redaction(secrets);
        return this with { Baseline = redactor.Package(Baseline), Edited = redactor.Package(Edited), QueryDraft = QueryDraft is null ? null : redactor.Package(new TestPackage { Queries = [QueryDraft] }).Queries[0] };
    }
}
