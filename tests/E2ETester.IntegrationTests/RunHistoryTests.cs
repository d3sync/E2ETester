using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class RunHistoryTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject)
    {
        var directory = Path.Combine(Path.GetTempPath(), "E2ETester-history", Guid.NewGuid().ToString("N"));
        var started = DateTimeOffset.UtcNow;
        using (var store = new SqliteRunStore(directory))
        {
            check((await store.ListAsync()).Count == 0 && await store.LoadAsync("missing") is null, "empty history and missing ID are handled");
            for (var i = 0; i < 55; i++) await store.SaveAsync(new TestRun { Id = $"run-{i:00}", StartedAt = started, Package = new() { Title = $"Session {i}" }, BaseUrl = "http://localhost:5188", FinishedAt = i == 0 ? started : null });
            var first = await store.ListAsync(0, 50); var second = await store.ListAsync(50, 50);
            check(first.Count == 50 && second.Count == 5 && first.Concat(second).Select(r => r.Id).Distinct().Count() == 55, "history pages have no gaps or overlaps");
            check(first[0].Id == "run-54" && second[^1].Id == "run-00", "same-time sessions have deterministic ordering");
            check(second[^1].FinishedAt == started && second[^1].Title == "Session 0", "history summaries retain completion and package details");
            check(await store.LoadAsync("' OR 1=1 --") is null, "history lookup uses bound IDs");
            await reject(() => store.ListAsync(-1), "history rejects negative offsets");
            await reject(() => store.ListAsync(0, 101), "history bounds page size");
            var pending = (await store.LoadAsync("run-01"))!;
            pending.Attempts.Add(new Attempt { CaseId = "interrupted" }); await store.SaveAsync(pending);
        }
        using var reopened = new SqliteRunStore(directory);
        var transport = new NoRequests(); var coordinator = new RunCoordinator(reopened, transport);
        var restored = await coordinator.ResumeAsync("run-01");
        check(restored?.Id == "run-01" && restored.Attempts.Single().State == AttemptState.Interrupted && transport.Count == 0, "selected older run recovers without HTTP replay");
        check((await reopened.ListAsync(50, 50)).Single(r => r.Id == "run-01").AttemptCount == 1, "summary counts reflect saved attempts after restart");
        var finished = (await coordinator.ResumeAsync("run-00"))!;
        check(finished.FinishedAt == started && Reports.Json(finished).Contains("run-00"), "finished historical run keeps completion and can export");
        await reject(() => coordinator.ExecuteAsync(finished, new(), new(), [], new("none", AuthKind.None), default), "finished historical run cannot execute requests");
        check(await coordinator.ResumeAsync("missing") is null && (await coordinator.ResumeAsync())?.Id == "run-54", "missing selection does not fall back to another run");
    }
    private sealed class NoRequests : IHttpTransport
    {
        public int Count { get; private set; }
        public Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken) { Count++; throw new InvalidOperationException("History must not replay HTTP."); }
    }
}
