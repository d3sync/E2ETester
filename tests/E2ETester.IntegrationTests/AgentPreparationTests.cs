using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class AgentPreparationTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string directory, TestServer server)
    {
        if (!OperatingSystem.IsWindows()) return;
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "agent-prepare.db"), Pooling = false }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE accounts(id INTEGER PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES(1,15)"; await command.ExecuteNonQueryAsync();
        }
        const string dbVariable = "E2ETESTER_TEST_PREP_DB", tokenVariable = "E2ETESTER_TEST_PREP_TOKEN";
        Environment.SetEnvironmentVariable(dbVariable, connectionString); Environment.SetEnvironmentVariable(tokenVariable, "old-token");
        var plan = new PreparationPlan { Provider = DatabaseProvider.Sqlite, ConnectionRef = "admin", Table = "accounts", ValueColumn = "permissions", ValueJson = "4", RequireFreshLogin = true };
        var target = new DatabasePreparationTarget(new("admin", DatabaseProvider.Sqlite, connectionString));
        var config = new AgentConfiguration { Workspace = Path.Combine(directory, "agent-prepare"), AllowHttp = true, AllowedBaseUrls = [server.Url], AllowPreparation = true, ApprovedPreparations = [plan], Databases = [new("admin", DatabaseProvider.Sqlite, dbVariable, true)], Identities = [new("tester", AuthKind.Bearer, tokenVariable)] };
        var package = new TestPackage { SchemaVersion = "1.5", Cases = [new() { Id = "case", Request = new() { Url = "/health", IdentityRef = "tester" }, Preparation = plan }] };
        string runId, operationId = Guid.NewGuid().ToString();
        static string Execution(object result) => JsonSerializer.SerializeToElement(result).GetProperty("executionId").GetString()!;
        try
        {
            await using (var workspace = new AgentWorkspace(config with { AllowPreparation = false }))
            {
                runId = (await workspace.StartAsync(PackageJson.Write(package), server.Url)).Id;
                await reject(() => workspace.PrepareEnvironmentAsync(runId, "case", operationId, default), "MCP preparation requires explicit host permission");
            }
            await using (var workspace = new AgentWorkspace(config with { ApprovedPreparations = [plan with { ValueJson = "8" }] }))
                await reject(() => workspace.PrepareEnvironmentAsync(runId, "case", operationId, default), "MCP preparation requires exact approved value and target");
            await using (var workspace = new AgentWorkspace(config))
            {
                var execution = Execution(await workspace.PrepareAsync(runId, "case"));
                await reject(() => workspace.ExecuteAsync(execution), "MCP cannot skip required preparation");
                await workspace.PrepareEnvironmentAsync(runId, "case", operationId, default);
                check(await target.ReadAsync(plan, default) == "4", "MCP applies approved preparation");
                execution = Execution(await workspace.PrepareAsync(runId, "case"));
                await reject(() => workspace.ExecuteAsync(execution), "MCP rejects previous JWT after preparation");
                await reject(() => workspace.PrepareEnvironmentAsync(runId, "case", Guid.NewGuid().ToString(), default), "MCP blocks a second pending preparation");
            }
            await using (var workspace = new AgentWorkspace(config))
            {
                await workspace.PrepareEnvironmentAsync(runId, "case", operationId, default);
                check(await target.ReadAsync(plan, default) == "4", "MCP stable operation ID survives restart");
                Environment.SetEnvironmentVariable(tokenVariable, "new-token");
                var execution = Execution(await workspace.PrepareAsync(runId, "case"));
                await workspace.ExecuteAsync(execution);
                for (var i = 0; i < 500 && JsonSerializer.SerializeToElement(await workspace.OperationAsync(execution)).GetProperty("state").GetString() == "running"; i++) await Task.Delay(20);
                check((await workspace.GetRunAsync(runId)).Attempts.Count == 1, "MCP executes after same identity receives fresh token");
                await target.ReplaceAsync(plan, "4", "8", default);
                await reject(() => workspace.FinishAsync(runId), "MCP finish refuses restoration conflict");
                check((await workspace.GetRunAsync(runId)).FinishedAt is null, "MCP run remains open after failed restoration");
                await target.ReplaceAsync(plan, "8", "4", default);
                await workspace.FinishAsync(runId);
                check(await target.ReadAsync(plan, default) == "15", "MCP finish restores original permissions");
                await workspace.PrepareEnvironmentAsync(runId, "case", operationId, default);
                check(await target.ReadAsync(plan, default) == "15", "MCP restored operation cannot reapply");
                check((await workspace.GetRunAsync(runId)).PreparationLog?.Last().Status == "restored", "MCP records restoration audit in report");
            }
            var rolePlan = plan with { JwtClaim = "permissions" };
            var rolePackage = package with { Cases = [package.Cases[0] with { Preparation = rolePlan }] };
            string Jwt(int permissions, int hours = 1) => "e30." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"permissions\":" + permissions + ",\"exp\":" + DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeSeconds() + "}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test";
            Environment.SetEnvironmentVariable(tokenVariable, Jwt(4));
            await using (var workspace = new AgentWorkspace(config with { Workspace = Path.Combine(directory, "cached-role"), AllowPreparation = false, Databases = [] }))
            {
                var roleRun = await workspace.StartAsync(PackageJson.Write(rolePackage), server.Url);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    var execution = Execution(await workspace.PrepareAsync(roleRun.Id, "case")); await workspace.ExecuteAsync(execution);
                    for (var i = 0; i < 500 && JsonSerializer.SerializeToElement(await workspace.OperationAsync(execution)).GetProperty("state").GetString() == "running"; i++) await Task.Delay(20);
                }
                check((await workspace.GetRunAsync(roleRun.Id)).Attempts.Count == 2 && await target.ReadAsync(plan, default) == "15", "MCP reuses matching permission JWT without database access or preparation writes");
                Environment.SetEnvironmentVariable(tokenVariable, Jwt(8));
                await reject(async () => await workspace.ExecuteAsync(Execution(await workspace.PrepareAsync(roleRun.Id, "case"))), "MCP rejects a mismatched permission JWT");
                Environment.SetEnvironmentVariable(tokenVariable, Jwt(4, -1));
                await reject(async () => await workspace.ExecuteAsync(Execution(await workspace.PrepareAsync(roleRun.Id, "case"))), "MCP rejects an expired permission JWT");
            }

        }
        finally { Environment.SetEnvironmentVariable(dbVariable, null); Environment.SetEnvironmentVariable(tokenVariable, null); }
    }
}
