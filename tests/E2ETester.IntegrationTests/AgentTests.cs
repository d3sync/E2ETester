using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class AgentTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, TestServer server)
    {
        var directory = Path.Combine(Path.GetTempPath(), "E2ETester-agent-tests", Guid.NewGuid().ToString("N"));
        var config = new AgentConfiguration { Workspace = directory, AllowHttp = true, AllowAuthoring = true, AllowedBaseUrls = [server.Url] };
        var package = new TestPackage { BaseUrl = server.Url, Cases = [new() { Id = "one", Request = new() { Url = "/health" }, Assertions = [new()] }] };
        string executionId, runId;
        static string Property(object value, string key) => JsonSerializer.SerializeToElement(value).GetProperty(key).GetString()!;
        async Task Wait(AgentWorkspace workspace, string id)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Property(await workspace.OperationAsync(id), "state") == "running") await Task.Delay(20, deadline.Token);
        }
        await using (var workspace = new AgentWorkspace(config))
        {
            await reject(() => workspace.StartAsync(PackageJson.Write(package), "http://example.com"), "agent rejects unapproved target");
            var run = await workspace.StartAsync(PackageJson.Write(package), server.Url); runId = run.Id;
            await reject(() => workspace.AuthenticateAsync("unknown"), "agent browser login requires explicit host permission");
            await workspace.SavePackageAsync(PackageJson.Write(package));
            var library = await workspace.ListPackagesAsync(); check((await workspace.GetPackageAsync(library[0].Id))?.Cases.Count == 1, "agent package authoring and export use shared library");
            var prepared = await workspace.PrepareAsync(runId, "one"); executionId = Property(prepared, "executionId");
            var before = server.Requests.Count;
            await workspace.ExecuteAsync(executionId); await workspace.ExecuteAsync(executionId); await Wait(workspace, executionId);
            var persisted = await workspace.GetRunAsync(runId);
            check(server.Requests.Count == before + 1 && persisted.Attempts.Single().ExecutionId == executionId, "agent duplicate execution sends once and records operation ID");
            check(persisted.Attempts[0].Verdict == Verdict.Pass, "agent shares desktop assertion engine");
            var stale = Property(await workspace.PrepareAsync(runId, "one"), "executionId");
            await workspace.VariationAsync(runId, "one", PackageJson.Write(new RequestDefinition { Url = "/slow" }), "Slow variation");
            await reject(() => workspace.ExecuteAsync(stale), "agent rejects stale package preparation");
            await reject(() => workspace.PrepareAsync(runId, "one", PackageJson.Write(new RequestDefinition { Url = "http://example.com" })), "agent rejects absolute URL override");
            await reject(() => workspace.PrepareAsync(runId, "one", PackageJson.Write(new RequestDefinition { Headers = [new() { Name = "Host", Value = "example.com" }] })), "agent rejects virtual-host override of allowed target");
            var slow = Property(await workspace.PrepareAsync(runId, "one", PackageJson.Write(new RequestDefinition { Url = "/slow" })), "executionId");
            await workspace.ExecuteAsync(slow);
            await reject(() => workspace.FinishAsync(runId), "agent cannot finish during active execution");
            workspace.Cancel(slow); await Wait(workspace, slow);
            check((await workspace.GetRunAsync(runId)).Attempts.Last().Verdict == Verdict.Inconclusive, "cancelled agent request never passes");
            var finish = Property(await workspace.PrepareAsync(runId, "one"), "executionId");
            await workspace.FinishAsync(runId);
            await reject(() => workspace.ExecuteAsync(finish), "agent cannot dispatch preparation after finish");
        }
        await using (var reopened = new AgentWorkspace(config))
        {
            var before = server.Requests.Count; await reopened.ExecuteAsync(executionId);
            check(server.Requests.Count == before, "agent execution ID survives host restart without replay");
        }
        await using (var denied = new AgentWorkspace(config with { Workspace = directory + "-denied", AllowHttp = false }))
            await reject(() => denied.StartAsync(PackageJson.Write(package), server.Url), "agent HTTP permission defaults closed");
        await using (var restricted = new AgentWorkspace(config with { Workspace = directory + "-restricted", AllowAuthoring = false, AllowedBaseUrls = [server.Url.TrimEnd('/') + "/api"] }))
        {
            var run = await restricted.StartAsync(PackageJson.Write(package), server.Url.TrimEnd('/') + "/api");
            await reject(() => restricted.PrepareAsync(run.Id, "one"), "agent path allowlist rejects sibling resource");
            await reject(() => restricted.VariationAsync(run.Id, "one", PackageJson.Write(package.Cases[0].Request), "denied"), "agent authoring permission enforced");
        }
        var query = new QueryDefinition { Id = "approved", Sql = "SELECT 1 AS value" };
        await using (var sql = new AgentWorkspace(config with { Workspace = directory + "-queries", AllowSavedQueries = true, SavedQueries = [query] }))
        {
            var forged = package with { Queries = [query with { Sql = "SELECT secret FROM secrets" }] };
            var run = await sql.StartAsync(PackageJson.Write(forged), server.Url);
            await reject(() => sql.QueryAsync(run.Id, query.Id), "agent cannot substitute SQL under an approved query ID");
        }
    }
}
