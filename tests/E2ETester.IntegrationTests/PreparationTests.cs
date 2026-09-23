using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class PreparationTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string directory)
    {
        var path = Path.Combine(directory, "prepare.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE accounts(id INTEGER PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES(1,15),(2,31)"; command.ExecuteNonQuery();
        }
        var plan = new PreparationPlan { Provider = DatabaseProvider.Sqlite, ConnectionRef = "write-db", Table = "accounts", KeyColumn = "id", KeyJson = "1", ValueColumn = "permissions", ValueJson = "4", RequireFreshLogin = true, Flags = [new("Read", 4), new("Write", 8)] };
        var target = new DatabasePreparationTarget(new("write-db", DatabaseProvider.Sqlite, connectionString));
        var entry = new PreparationRecovery { Plan = plan, OriginalJson = await target.ReadAsync(plan, default) };
        string saved = ""; var saves = 0;
        Task Save() { saves++; saved = PackageJson.Write(entry); return Task.CompletedTask; }
        await Preparations.ApplyAsync(entry, target, Save, default);
        check(saves == 2 && entry.Status == "applied" && await target.ReadAsync(plan, default) == "4", "preparation persists before and after a single-row change");
        check(await target.ReadAsync(plan with { KeyJson = "2" }, default) == "31", "preparation does not alter other users");
        var recovered = PackageJson.Read<PreparationRecovery>(saved);
        await Preparations.RestoreAsync(recovered, target, () => Task.CompletedTask, default);
        check(recovered.Status == "restored" && await target.ReadAsync(plan, default) == "15", "restarted preparation restores the exact original value");
        await Preparations.RestoreAsync(recovered, target, () => Task.CompletedTask, default);
        check(await target.ReadAsync(plan, default) == "15", "restoration is idempotent");
        await reject(() => Preparations.ApplyAsync(entry, target, () => throw new IOException("disk full"), default), "recovery persistence failure prevents database write");
        check(await target.ReadAsync(plan, default) == "15", "database is unchanged when recovery cannot be recorded");
        await target.ReplaceAsync(plan, "15", "4", default);
        var interrupted = entry with { Status = "pending" };
        await Preparations.RestoreAsync(interrupted, target, () => Task.CompletedTask, default);
        check(await target.ReadAsync(plan, default) == "15", "uncertain apply can recover after an interruption");
        await target.ReplaceAsync(plan, "15", "7", default);
        await reject(() => Preparations.RestoreAsync(entry, target, () => Task.CompletedTask, default), "restoration does not overwrite concurrent changes");
        await target.ReplaceAsync(plan, "7", "15", default);
        await reject(() => target.ReplaceAsync(plan, "99", "4", default), "stale preparation snapshot cannot overwrite a row");
        await reject(() => target.ReadAsync(plan with { Table = "accounts; DROP TABLE accounts" }, default), "preparation rejects SQL in identifiers");
        await reject(() => target.ReadAsync(plan with { KeyJson = "99" }, default), "preparation rejects a missing user");
        var readOnly = new DatabasePreparationTarget(new("write-db", DatabaseProvider.Sqlite, connectionString, true));
        await reject(() => readOnly.ReplaceAsync(plan, "15", "4", default), "read-only profiles cannot be reused for preparation writes");
        foreach (var provider in Enum.GetValues<DatabaseProvider>())
        {
            var details = new DatabaseConnectionDetails { Provider = provider, File = path, Database = "example", Username = "tester" };
            var built = details.Build("p;word=\"x");
            check(built.Length > 0 && !PackageJson.Write(details).Contains("p;word"), $"{provider} connection builder handles password separately from templates");
        }
        var http = new PreparationHttp();
        var apiPlan = new PreparationPlan { Kind = PreparationKind.ApiValue, ValueJson = "4", CaptureRequest = new() { Url = "/account" }, UpdateRequest = new() { Method = "PATCH", Url = "/account", SendBody = true, Body = "{\"permissions\":{{valueJson}}}" } };
        var api = new ApiPreparationTarget("https://api.example.test", http, (_, _) => Task.FromResult(new IdentitySession("none", AuthKind.None)));
        var apiEntry = new PreparationRecovery { Plan = apiPlan, OriginalJson = await api.ReadAsync(apiPlan, default) };
        await Preparations.ApplyAsync(apiEntry, api, () => Task.CompletedTask, default);
        check(http.Value == 4 && http.LastIfMatch == "v1", "API preparation applies with captured ETag");
        await Preparations.RestoreAsync(apiEntry, api, () => Task.CompletedTask, default);
        check(http.Value == 15, "API preparation uses original scalar for restoration");
        http.WithEtag = false;
        await reject(() => api.ReplaceAsync(apiPlan, "15", "4", default), "API without concurrency support requires an explicit package opt-in");
        await api.ReplaceAsync(apiPlan with { AllowUnconditionalApiUpdate = true }, "15", "4", default);
        check(http.Value == 4, "API can explicitly allow unconditional updates");
        var package = new TestPackage { SchemaVersion = "1.5", Cases = [new TestCase { Preparation = plan }] };
        check(PackageJson.Import(PackageJson.Write(package)).Cases[0].Preparation!.Flags.Count == 2, "preparation survives package import and export");
        using var store = new SqliteRunStore(Path.Combine(directory, "preparation-run"));
        var coordinator = new RunCoordinator(store, http); var run = await coordinator.StartAsync(package, package.BaseUrl);
        await reject(() => coordinator.ExecuteAsync(run, package.Cases[0], package.Cases[0].Request, [], new("none", AuthKind.None), default), "MCP and unprepared execution cannot bypass preparation");
        run.PreparationLog = [new(DateTimeOffset.UtcNow, package.Cases[0].Id, plan.Title, "restored")];
        check(Reports.Create(run).SchemaVersion == "1.8" && Reports.Text(run).Contains("PREPARATION AND RESTORATION"), "report includes preparation and restoration audit");
        await File.WriteAllTextAsync(Path.Combine(directory, "preparation-package.json"), PackageJson.Write(package));
        await File.WriteAllTextAsync(Path.Combine(directory, "preparation-report.json"), Reports.Json(run));
    }
    private sealed class PreparationHttp : IHttpTransport
    {
        public long Value = 15; public bool WithEtag = true; public string? LastIfMatch;
        public Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken)
        {
            if (request.Method != "GET")
            {
                LastIfMatch = request.Headers.FirstOrDefault(h => h.Name == "If-Match")?.Value;
                using var json = JsonDocument.Parse(request.Body); Value = json.RootElement.GetProperty("permissions").GetInt64();
            }
            return Task.FromResult(new ResponseSnapshot { StatusCode = 200, Body = $"{{\"permissions\":{Value}}}", Headers = WithEtag ? new() { ["ETag"] = ["v1"] } : [] });
        }
    }
}
