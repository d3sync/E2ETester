using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Desktop;
internal static class GuidedActionsSmoke
{
    internal static async Task RunAsync(Window owner)
    {
        var root = Path.Combine(Path.GetTempPath(), "E2E-actions-smoke", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "fixture.db") }.ToString();
        using (var db = new SqliteConnection(cs)) { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE account(id INTEGER PRIMARY KEY, permissions INTEGER); INSERT INTO account VALUES(1,15)"; cmd.ExecuteNonQuery(); }
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var url = $"http://127.0.0.1:{number}";
        using var listener = new HttpListener(); listener.Prefixes.Add(url + "/"); listener.Start();
        var plan = new PreparationPlan { Provider = DatabaseProvider.Sqlite, ConnectionRef = "admin", Table = "account", KeyJson = "1", ValueColumn = "permissions", ValueJson = "4" };
        var cleanup = new TestCase { Id = "cleanup", Title = "Remove created item", Request = new() { Method = "DELETE", Url = "/items/{{case.create./id}}", IdentityRef = "none" }, Assertions = [new() { Expected = "204" }] };
        var permissions = new TestCase { Id = "permissions", Title = "Choose permissions", Preparation = plan };
        var package = new TestPackage { SchemaVersion = "1.6", Id = "actions-demo", BaseUrl = url, Cases = [new() { Id = "create" }], GuidedActions = [cleanup, permissions] };
        var file = Path.Combine(root, "actions.jsee"); File.WriteAllText(file, PackageJson.Write(package));
        var workspace = Path.Combine(root, "workspace");
            var source = new TestRun { Package = package, BaseUrl = url, Attempts = [new() { CaseId = "create", State = AttemptState.Completed, Verdict = Verdict.Pass, Response = new() { StatusCode = 201, Body = "{\"id\":123}" } }] };
            source.Attempts.Add(new Attempt { CaseId = "create", State = AttemptState.Completed, Verdict = Verdict.Fail, Response = new() { StatusCode = 400, Body = "UICC already exists." } });
        using (var seed = new E2ETester.Infrastructure.SqliteRunStore(workspace)) await seed.SaveAsync(source);
        using (var model = new MainViewModel(workspace))
        {
            model.OpenPackageFile(file); model.Database.Connections.Add(new() { Alias = "admin", Provider = DatabaseProvider.Sqlite, ConnectionString = cs });
            var found = await model.FindGuidedActionSourceAsync(cleanup.Request);
            if (found?.Id != source.Id) throw new InvalidOperationException("Could not find the prior saved resource response.");
            var receive = listener.GetContextAsync();
            var send = model.ExecuteGuidedRequestAsync(cleanup, cleanup.Request, source);
            var context = await receive.WaitAsync(TimeSpan.FromSeconds(10));
            if (context.Request.HttpMethod != "DELETE" || context.Request.RawUrl != "/items/123") throw new InvalidOperationException("Cleanup did not target the captured resource.");
            context.Response.StatusCode = 204; context.Response.Close();
            if (!(await send).StartsWith("Pass") || model.GuidedActionSource is not null) throw new InvalidOperationException("Cleanup affected suite execution or failed.");
            await model.ApplyGuidedPermissionsAsync(permissions, plan, "none");
            if (!model.HasPendingPreparation || model.GuidedActionSource!.Attempts.Count != 0) throw new InvalidOperationException("Permission action changed case coverage or lost recovery.");
            model.SelectedIdentity!.Secret = "guidance-token"; await model.Send.RunAsync();
            if (model.SelectedAttempt?.Verdict != Verdict.Pass) throw new InvalidOperationException("Applied action blocked the normal sign-in case: " + model.Message);
            File.WriteAllText(Path.Combine(root, "guided-report.json"), Reports.Json(model.GuidedActionSource!));
            var dialog = new GuidedActionsWindow(model, package.GuidedActions) { Owner = owner }; dialog.Show(); dialog.Close();
        }
        using (var reopened = new MainViewModel(workspace))
        {
            reopened.OpenPackageFile(file); reopened.Database.Connections.Add(new() { Alias = "admin", Provider = DatabaseProvider.Sqlite, ConnectionString = cs });
            if (!reopened.HasPendingPreparation) throw new InvalidOperationException("Action recovery did not survive restart.");
            await reopened.RestorePreparation.RunAsync();
            if (reopened.HasPendingPreparation) throw new InvalidOperationException("Could not restore action: " + reopened.Message);
        }
        using (var db = new SqliteConnection(cs)) { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT permissions FROM account WHERE id=1"; if ((long)cmd.ExecuteScalar()! != 15) throw new InvalidOperationException("Action restoration lost the original permission value."); }
    }
}
