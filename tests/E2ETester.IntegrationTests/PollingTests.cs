using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class PollingTests
{
    public static async Task RunAsync(Action<bool, string> check, string baseUrl)
    {
        var query = new QueryDefinition { Id = "poll" };
        var scalar = new QueryEvidence { Query = query, Result = new() { Rows = [new() { ["value"] = JsonSerializer.SerializeToElement(3) }] } };
        foreach (var (kind, expected) in new[] { (DatabaseCheckKind.ScalarGreaterThan, "2"), (DatabaseCheckKind.ScalarLessThan, "4"), (DatabaseCheckKind.ScalarNotEquals, "\"3\"") })
            check(DatabaseQueries.Assess(new() { Kind = kind, Column = "value", ExpectedJson = expected }, scalar).Verdict == Verdict.Pass, "typed database operator " + kind);
        check(DatabaseQueries.Assess(new() { Kind = DatabaseCheckKind.RowExists }, scalar).Verdict == Verdict.Pass, "row exists check");
        check(DatabaseQueries.Assess(new() { Kind = DatabaseCheckKind.RowAbsent }, scalar with { Result = new() }).Verdict == Verdict.Pass, "row absent check");
        using var store = new SqliteRunStore(Path.Combine(Path.GetTempPath(), "E2ETester-poll", Guid.NewGuid().ToString("N")));
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var test = new TestCase { Request = new() { Url = "/health" }, Assertions = [new()], DatabaseChecks = [new() { QueryId = query.Id, Kind = DatabaseCheckKind.RowExists, PollTimeoutSeconds = 2, PollIntervalMilliseconds = 100 }] };
        var run = await coordinator.StartAsync(new() { Cases = [test], Queries = [query] }, baseUrl);
        var attempt = await coordinator.ExecuteAsync(run, test, test.Request, test.Assertions, new("none", AuthKind.None), default);
        var executor = new EventuallyVisible();
        await coordinator.VerifyDatabaseAsync(run, attempt, new(executor), [new("local-db", DatabaseProvider.Sqlite, "")], default);
        check(executor.Count == 3 && attempt.Verdict == Verdict.Pass, "database polling stops on first passing result");
        check(attempt.DatabaseEvidence.Count == 3 && attempt.DatabaseEvidence[0].Assessment?.Verdict == Verdict.Fail, "polling retains every unsuccessful observation");
        check(Reports.Create(run).Summary.HadFailures, "eventual success preserves historical database failures");
    }
    private sealed class EventuallyVisible : IDatabaseExecutor
    {
        public int Count { get; private set; }
        public Task<QueryResult> ExecuteAsync(QueryDefinition query, DatabaseConnectionProfile connection, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new QueryResult { Rows = Count < 3 ? [] : [new() { ["value"] = JsonSerializer.SerializeToElement(1) }] });
        }
    }
}
