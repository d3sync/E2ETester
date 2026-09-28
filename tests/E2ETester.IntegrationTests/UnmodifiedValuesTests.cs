using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class UnmodifiedValuesTests
{
    internal static async Task RunAsync(Action<bool, string> check, string directory, string baseUrl)
    {
        const string literal = "[REDACTED]";
        const string body = "{\"groupToken\":\"original-group-value\",\"password\":\"original-password-value\",\"note\":\"[REDACTED]\"}";
        var test = new TestCase { Id = "echo", Request = new() { Url = "/echo", Method = "POST", SendBody = true, Body = body, BodySensitive = true, IdentityRef = "none", Headers = [new() { Name = "X-Value", Value = literal, Sensitive = true }] }, Assertions = [new() { Kind = CheckKind.JsonEquals, Selector = "/note", Expected = JsonSerializer.Serialize(literal) }] };
        using var store = new SqliteRunStore(Path.Combine(directory, "unchanged-values"));
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var run = await coordinator.StartAsync(new TestPackage { Cases = [test] }, baseUrl);
        var attempt = await coordinator.ExecuteAsync(run, test, test.Request, test.Assertions, new("none", AuthKind.None, ""), default);
        check(attempt.Verdict == Verdict.Pass && attempt.Response?.Body == body && attempt.Request.Body == body, "Responses and requests preserve sensitive values and literal placeholder text unchanged");
        check(ValueBindings.Resolve("{{case.echo./note}}", null, run: run).GetString() == literal, "Response bindings do not reject literal placeholder text");
        check(Reports.Json((await store.LoadAsync(run.Id))!).Contains("original-group-value"), "Saved run and report retain original groupToken");
        var dbPath = Path.Combine(directory, "literal-values.db");
        using (var db = new SqliteConnection("Data Source=" + dbPath)) { db.Open(); }
        var query = new QueryDefinition { ConnectionRef = "db", Provider = DatabaseProvider.Sqlite, Sql = "SELECT @value AS value", Parameters = [new() { Name = "value", Value = literal }] };
        var evidence = await new DatabaseQueries(new DatabaseExecutor()).ExecuteAsync(query, new("db", DatabaseProvider.Sqlite, "Data Source=" + dbPath), default);
        check(evidence.Result.Error is null && evidence.Result.Rows.Single()["value"].GetString() == literal, "Query parameters retain literal placeholder text");
        check(DatabaseQueries.Assess(new() { Kind = DatabaseCheckKind.ScalarEquals, Column = "value", ExpectedJson = JsonSerializer.Serialize(literal) }, evidence).Verdict == Verdict.Pass, "Database checks compare actual values without redaction exceptions");
        check(ValueBindings.Resolve("{{fixture.value}}", DatabaseQueries.SelectFixture(evidence, 0)).GetString() == literal, "Fixture bindings retain literal placeholder text");
    }
}
