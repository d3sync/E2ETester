using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class DraftTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "E2ETester-drafts", Guid.NewGuid().ToString("N"));
        var original = new TestPackage { Title = "Draft package", Cases = [new TestCase { Id = "draft-case", Request = new() { Query = [new() { Name = "page", Value = "1" }], Headers = [new() { Name = "Authorization", Value = "draft-secret-canary" }] } }] };
        var edited = PackageJson.Clone(original); edited.Cases[0].Request.Query[0].Value = "2";
        edited.Cases[0].Request.Body = "{ unfinished JSON";
        edited.Description = "Session credential: session-secret-canary";
        edited.BaseUrl = "https://user:password@example.test";
        var draft = new WorkspaceDraft { Baseline = original, Edited = edited, SelectedCaseId = "draft-case", RunId = "linked-run" };
        using (var store = new SqliteRunStore(directory))
        {
            check(await store.LoadDraftAsync() is null, "empty workspace has no draft");
            await store.SaveDraftAsync(draft, ["session-secret-canary"]);
            check(original.Cases[0].Request.Headers[0].Value == "draft-secret-canary", "saving a redacted draft does not mutate in-memory input");
        }
        using var reopened = new SqliteRunStore(directory);
        var restored = (await reopened.LoadDraftAsync())!;
        check(restored.Baseline.Cases[0].Request.Query[0].Value == "1" && restored.Edited.Cases[0].Request.Query[0].Value == "2", "draft preserves baseline and edited values separately across restart");
        check(restored.Edited.Cases[0].Request.Body == "{ unfinished JSON", "draft accepts unfinished content without executing or validating it");
        check(restored.RunId == "linked-run" && restored.SelectedCaseId == "draft-case", "draft retains run linkage and selected case");
        var json = PackageJson.Write(restored);
        check(!json.Contains("draft-secret-canary") && !json.Contains("session-secret-canary") && !json.Contains("user:password"), "draft redacts credentials, known session values and URL userinfo");
        await reopened.SaveDraftAsync(restored with { RunId = null, Edited = edited with { Title = "Second save" } }, ["session-secret-canary"]);
        check((await reopened.LoadDraftAsync())!.Edited.Title == "Second save", "draft save atomically replaces the previous workspace draft");
        check((await reopened.ListAsync()).Count == 0, "draft persistence never creates execution history");
    }
}
