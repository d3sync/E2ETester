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

    public WorkspaceDraft Snapshot()
    {
        return this with { Baseline = PackageJson.Clone(Baseline), Edited = PackageJson.Clone(Edited), QueryDraft = QueryDraft is null ? null : PackageJson.Clone(new TestPackage { Queries = [QueryDraft] }).Queries[0] };
    }
}
