using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class DatabaseTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string serverUrl)
    {
        var root = Path.Combine(Path.GetTempPath(), "E2ETester-db-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "fixtures.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using (var seed = new SqliteConnection(connectionString))
        {
            seed.Open(); using var command = seed.CreateCommand();
            command.CommandText = "CREATE TABLE fixture (id INTEGER PRIMARY KEY, name TEXT, enabled INTEGER, note TEXT, password TEXT); INSERT INTO fixture VALUES (1, 'Example fixture', 1, NULL, 'db-canary-credential'), (2, 'Second fixture', 0, '', 'another-secret');";
            command.ExecuteNonQuery();
        }
        var profile = new DatabaseConnectionProfile("fixture-db", DatabaseProvider.Sqlite, connectionString);
        var executor = new DatabaseExecutor(); var queries = new DatabaseQueries(executor);
        check((await executor.TestConnectionAsync(profile, default)).ErrorCode is null, "SQLite connectivity probe opens the existing database");
        var badProbe = await executor.TestConnectionAsync(profile with { ConnectionString = "invalid-secret-setting=private-sentinel" }, default);
        check(badProbe.ErrorCode == "connection_failed" && !badProbe.Error!.Contains("private-sentinel"), "connection diagnostics identify failure without leaking settings");
        var query = new QueryDefinition { Id = "fixtures", ConnectionRef = "fixture-db", Sql = "SELECT id, name, enabled, note, password FROM fixture ORDER BY id" };
        var evidence = await queries.ExecuteAsync(query, profile, default);
        check(evidence.Result.ErrorCode is null && evidence.Result.Rows.Count == 2, "SQLite query returns fixture rows");
        check(evidence.Result.Rows[0]["id"].ValueKind == JsonValueKind.Number && evidence.Result.Rows[0]["note"].ValueKind == JsonValueKind.Null && evidence.Result.Rows[1]["note"].GetString() == "", "database types preserve number, null and empty string");
        check(!PackageJson.Write(evidence).Contains("db-canary-credential"), "sensitive SQL columns redacted");
        await reject(() => Task.Run(() => DatabaseQueries.SelectFixture(evidence, -1)), "fixture needs explicit valid selection");
        var fixture = DatabaseQueries.SelectFixture(evidence, 0);
        var template = new RequestDefinition { Url = "/echo", Method = "POST", SendBody = true, Body = "{\"id\":\"{{fixture.id}}\",\"name\":\"{{fixture.name}}\",\"note\":\"{{fixture.note}}\"}" };
        var resolved = ValueBindings.Request(template, fixture);
        using (var body = JsonDocument.Parse(resolved.Body)) check(body.RootElement.GetProperty("id").ValueKind == JsonValueKind.Number && body.RootElement.GetProperty("note").ValueKind == JsonValueKind.Null, "JSON fixture bindings preserve types");
        await reject(() => Task.Run(() => ValueBindings.Request(template, null)), "missing fixture blocks preparation");
        await reject(() => Task.Run(() => ValueBindings.Resolve("{{fixture.password}}", fixture)), "redacted fixture cannot be used as data");
        var parameterized = query with { Sql = "SELECT id FROM fixture WHERE name = @name", Parameters = [new() { Name = "name", Value = "' OR 1=1 --" }] };
        var injection = await queries.ExecuteAsync(parameterized, profile, default);
        check(injection.Result.ErrorCode is null && injection.Result.Rows.Count == 0, "SQL parameters are bound instead of interpolated");
        var typed = query with { Sql = "SELECT @id AS id, @note AS note, @flag AS flag", Parameters = [new() { Name = "id", Type = ParameterType.Int64, Value = "42" }, new() { Name = "note", IsNull = true }, new() { Name = "flag", Type = ParameterType.Boolean, Value = "true" }] };
        var typedResult = await queries.ExecuteAsync(typed, profile, default);
        check(typedResult.Result.ErrorCode is null && typedResult.Result.Rows[0]["id"].GetInt64() == 42 && typedResult.Result.Rows[0]["note"].ValueKind == JsonValueKind.Null, "typed and null SQL parameters execute");
        var capped = await queries.ExecuteAsync(query with { RowLimit = 1 }, profile, default);
        check(capped.Result.Truncated && DatabaseQueries.Assess(new(), capped).Verdict == Verdict.Inconclusive, "row caps cannot produce passing DB checks");
        await reject(() => Task.Run(() => DatabaseQueries.SelectFixture(capped, 0)), "capped fixture query cannot be selected");
        var large = await queries.ExecuteAsync(query with { Sql = "SELECT printf('%20000s', 'x') AS text" }, profile, default);
        check(large.Result.Truncated, "large SQL cell is capped and marked incomplete");
        var duplicate = await queries.ExecuteAsync(query with { Sql = "SELECT 1 AS id, 2 AS id" }, profile, default);
        check(duplicate.Result.ErrorCode == "duplicate_columns", "duplicate SQL column names are rejected");
        var bad = await queries.ExecuteAsync(query with { Sql = "SELECT * FROM missing_table" }, profile, default);
        check(bad.Result.ErrorCode == "database_error" && DatabaseQueries.Assess(new(), bad).Verdict == Verdict.Inconclusive, "SQL failure is inconclusive");
        var write = await queries.ExecuteAsync(query with { Sql = "DELETE FROM fixture" }, profile, default);
        var afterWrite = await queries.ExecuteAsync(query, profile, default);
        check(write.Result.ErrorCode is not null && afterWrite.Result.Rows.Count == 2, "SQLite writes are denied by the connection");
        var pragma = await queries.ExecuteAsync(query with { Sql = "PRAGMA query_only=OFF" }, profile, default);
        check(pragma.Result.ErrorCode is not null, "SQLite read-only mode cannot be disabled by query text");
        var attach = await queries.ExecuteAsync(query with { Sql = "ATTACH DATABASE ':memory:' AS other" }, profile, default);
        check(attach.Result.ErrorCode is not null, "SQLite queries cannot attach writable databases");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var result = await queries.ExecuteAsync(query, profile, cancelled.Token);
            check(result.Result.ErrorCode == "cancelled", "database cancellation is recorded");
        }
        var limited = query with { Sql = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 100000000) SELECT sum(x) FROM n", TimeoutSeconds = 1 };
        var timed = await queries.ExecuteAsync(limited, profile, default);
        check(timed.Result.ErrorCode == "timeout" && timed.Result.DurationMs < 5000, "long SQLite query honours a bounded timeout");
        var scalarQuery = new QueryDefinition { Id = "verify", ConnectionRef = "fixture-db", Sql = "SELECT name FROM fixture WHERE id = @id", Parameters = [new() { Name = "id", Type = ParameterType.Int64, Value = "{{response./id}}" }] };
        var dbCheck = new DatabaseCheck { Id = "name-check", QueryId = "verify", Kind = DatabaseCheckKind.ScalarEquals, Column = "name", ExpectedJson = "\"Example fixture\"" };
        var testCase = new TestCase { Id = "db-case", Title = "Fixture to API to database", Request = template, FixtureQueryId = "fixtures", Assertions = [new()], DatabaseChecks = [dbCheck] };
        var package = new TestPackage { SchemaVersion = "1.1", Cases = [testCase], Queries = [query, scalarQuery] };
        check(PackageJson.Import(PackageJson.Write(package)).Queries.Count == 2, "database package round-trips");
        using var store = new SqliteRunStore(Path.Combine(root, "journal"));
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var run = await coordinator.StartAsync(package, serverUrl);
        await reject(() => coordinator.ExecuteAsync(run, testCase, template, testCase.Assertions, new("none", AuthKind.None), default), "required fixture blocks HTTP without selection");
        var attempt = await coordinator.ExecuteAsync(run, testCase, template, testCase.Assertions, new("none", AuthKind.None), default, fixture);
        check(attempt.Response?.StatusCode == 200 && attempt.Verdict == Verdict.Inconclusive, "API success waits for required database checks");
        await coordinator.VerifyDatabaseAsync(run, attempt, queries, [profile], default);
        check(attempt.Verdict == Verdict.Pass && attempt.DatabaseEvidence.Count == 1, "response capture drives parameterized database verification");
        using (var mutation = new SqliteConnection(connectionString)) { mutation.Open(); using var command = mutation.CreateCommand(); command.CommandText = "UPDATE fixture SET name='Changed' WHERE id=1"; command.ExecuteNonQuery(); }
        await coordinator.VerifyDatabaseAsync(run, attempt, queries, [profile], default);
        check(attempt.Verdict == Verdict.Fail && attempt.DatabaseEvidence.Count == 2 && attempt.DatabaseEvidence[0].Assessment?.Verdict == Verdict.Pass, "DB change causes failure without erasing earlier evidence");
        await coordinator.AttachDatabaseEvidenceAsync(run, attempt, evidence);
        check(attempt.Verdict == Verdict.Fail && attempt.DatabaseEvidence.Last().Assessment is null, "exploratory evidence does not override checks");
        await coordinator.VerifyDatabaseAsync(run, attempt, queries, [], default);
        check(attempt.Verdict == Verdict.Inconclusive && attempt.DatabaseEvidence.Last().Result.ErrorCode == "prerequisite_unavailable", "missing DB profile does not claim API failure");
        var loaded = await coordinator.ResumeAsync();
        check(loaded?.Attempts[0].DatabaseEvidence.Count == 4 && loaded.Attempts[0].Fixture?.RowIndex == 0, "fixtures and DB evidence survive restart");
        var report = Reports.Json(run);
        check(report.Contains("databaseEvidence") && !report.Contains("db-canary-credential") && !report.Contains(connectionString), "reports include DB evidence but exclude credentials");
        check(report.Contains("\"hadFailures\": true"), "historical database failure remains in report summary");
        attempt.DatabaseEvidence.Add(new QueryEvidence { Query = scalarQuery, CheckId = dbCheck.Id, Purpose = "verification", Result = new QueryResult { ErrorCode = "running" } });
        await store.SaveAsync(run);
        var interrupted = await coordinator.ResumeAsync();
        check(interrupted?.Attempts[0].DatabaseEvidence.Last().Result.ErrorCode == "interrupted" && interrupted.Attempts[0].Verdict == Verdict.Inconclusive, "interrupted database verification recovers without replay");
        var unavailableStore = new RejectingDatabaseStore();
        var noQueryExecutor = new CountingDatabaseExecutor();
        await reject(() => new RunCoordinator(unavailableStore, new HttpTransport()).VerifyDatabaseAsync(run, attempt, new DatabaseQueries(noQueryExecutor), [profile], default), "unrecordable DB verification is rejected");
        check(noQueryExecutor.Count == 0, "journal must save before database verification executes");
        foreach (var provider in new[] { DatabaseProvider.SqlServer, DatabaseProvider.MySql, DatabaseProvider.PostgreSql })
        {
            var q = query with { Provider = provider }; var p = profile with { Provider = provider, ConnectionString = "" };
            var unavailable = await queries.ExecuteAsync(q, p, default);
            check(unavailable.Result.ErrorCode == "read_only_account_required", provider + " requires read-only account acknowledgement before connecting");
            using var adapter = DatabaseExecutor.CreateConnection(p);
            check(adapter is not null, provider + " driver constructs successfully (not a live DB test)");
        }
        using var optionalStore = new SqliteRunStore(Path.Combine(root, "optional-journal"));
        var optionalCoordinator = new RunCoordinator(optionalStore, new HttpTransport());
        var optionalCase = new TestCase { Id = "optional", FixtureQueryId = query.Id, Request = template, Assertions = [new()] };
        var optionalRun = await optionalCoordinator.StartAsync(new TestPackage { Cases = [optionalCase], Queries = [query] }, serverUrl);
        var manual = template with { Body = "{\"id\":1,\"note\":null}" };
        var manualAttempt = await optionalCoordinator.ExecuteAsync(optionalRun, optionalCase, manual, optionalCase.Assertions, new("none", AuthKind.None), default);
        check(manualAttempt.Response?.StatusCode == 200 && manualAttempt.Fixture is null && manualAttempt.Verdict == Verdict.Pass, "optional fixture permits manual values without a database selection");
        var explicitRequired = optionalCase with { FixtureRequired = true };
        await reject(() => optionalCoordinator.ExecuteAsync(optionalRun, explicitRequired, manual, optionalCase.Assertions, new("none", AuthKind.None), default), "explicitly required fixture blocks even a manually completed request");
        await reject(() => optionalCoordinator.ExecuteAsync(optionalRun, optionalCase, template, optionalCase.Assertions, new("none", AuthKind.None), default), "optional fixture never sends unresolved placeholders");
        var selectedAttempt = await optionalCoordinator.ExecuteAsync(optionalRun, optionalCase, template, optionalCase.Assertions, new("none", AuthKind.None), default, fixture);
        check(selectedAttempt.Fixture is not null && selectedAttempt.Response?.StatusCode == 200, "optional fixture is used and recorded when selected");
        check(PackageJson.Import(PackageJson.Write(new TestPackage { Cases = [explicitRequired], Queries = [query] })).Cases[0].FixtureRequired, "fixture requirement round-trips through package JSON");
        check(!ValueBindings.RequiresFixture(new RequestDefinition { Query = [new() { Value = "{{fixture.id}}", Enabled = false }], Body = "{{fixture.id}}", SendBody = false }), "disabled fields and unsent bodies do not require a fixture");
        var foreign = profile with { Alias = "wrong-db" };
        await reject(() => queries.ExecuteAsync(query, foreign, default), "wrong connection alias rejected");
    }
    private sealed class RejectingDatabaseStore : IRunStore
    {
        public Task SaveAsync(TestRun run) => throw new IOException("Simulated journal failure");
        public Task<TestRun?> LoadLatestAsync() => Task.FromResult<TestRun?>(null);
    }
    private sealed class CountingDatabaseExecutor : IDatabaseExecutor
    {
        public int Count { get; private set; }
        public Task<QueryResult> ExecuteAsync(QueryDefinition query, DatabaseConnectionProfile connection, CancellationToken cancellationToken) { Count++; return Task.FromResult(new QueryResult()); }
    }
}
