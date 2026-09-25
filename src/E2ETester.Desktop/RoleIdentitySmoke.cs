using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using E2ETester.Application;
using E2ETester.Core;
namespace E2ETester.Desktop;
internal static class RoleIdentitySmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "E2E-roles", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var url = $"http://127.0.0.1:{number}";
        using var server = new HttpListener(); server.Prefixes.Add(url + "/"); server.Start();
        var package = SuiteDefaults.Create(); package.BaseUrl = url;
        PreparationPlan Plan(string value) => new() { Table = "accounts", KeyJson = "1", ValueColumn = "permissions", ValueJson = value, RequireFreshLogin = true, JwtClaim = "permissions" };
        foreach (var (id, alias, value) in new[] { ("admin-one", "cloud-clientadmin", "4"), ("viewer", "cloud-viewonly", "8"), ("admin-two", "cloud-clientadmin", "4") })
            package.Cases.Add(new() { Id = id, Request = new() { Url = "/" + id, IdentityRef = alias }, Assertions = [new()], Preparation = Plan(value) });
        var file = Path.Combine(root, "roles.jsee"); File.WriteAllText(file, PackageJson.Write(package));
        string Jwt(int permissions, int hours = 2) => "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"permissions\":" + permissions + ",\"exp\":" + DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeSeconds() + "}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test";
        var userJwt = Jwt(15); var adminJwt = Jwt(4); var viewerJwt = Jwt(8);
        async Task Send(MainViewModel model, string id, string token)
        {
            model.SelectedCase = model.Cases.Single(c => c.Baseline.Id == id);
            var receive = server.GetContextAsync(); var sending = model.Send.RunAsync();
            var context = await receive.WaitAsync(TimeSpan.FromSeconds(10));
            if (context.Request.Headers["Authorization"] != "Bearer " + token) throw new InvalidOperationException("Wrong named identity sent for " + id);
            var body = Encoding.UTF8.GetBytes("ok"); context.Response.StatusCode = 200; context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body); context.Response.Close(); await sending;
            if (model.SelectedAttempt?.Verdict != Verdict.Pass) throw new InvalidOperationException("Role reuse blocked: " + model.Message);
        }
        for (var restart = 0; restart < 2; restart++)
        {
            using var model = new MainViewModel(Path.Combine(root, "workspace-" + restart), Path.Combine(root, "sessions")); model.OpenPackageFile(file);
            if (restart == 0)
                foreach (var (alias, jwt) in new[] { ("cloud-user", userJwt), ("cloud-clientadmin", adminJwt), ("cloud-viewonly", viewerJwt) })
                { var identity = model.Identities.Single(i => i.Alias == alias); identity.Kind = AuthKind.Bearer; identity.Secret = jwt; model.RememberIdentity(identity); }
            model.SelectedCase = model.Cases[0]; await model.Send.RunAsync();
            await Send(model, model.Cases[1].Baseline.Id, userJwt);
            await Send(model, "admin-one", adminJwt);
            // No database connection is configured: Prepare must reuse the already matching JWT.
            await model.PrepareCase.RunAsync();
            if (!model.Message.Contains("already has the required permissions")) throw new InvalidOperationException("Prepare tried to repeat work for a cached role.");
            await Send(model, "viewer", viewerJwt); await Send(model, "admin-two", adminJwt);
            if (model.HasPendingPreparation || model.Identities.Any(i => i.IsSnapshot)) throw new InvalidOperationException("Cached roles created preparations or timestamp identities.");
            var admin = model.Identities.Single(i => i.Alias == "cloud-clientadmin"); admin.Secret = viewerJwt;
            await model.Send.RunAsync(); if (!model.Message.Contains("needs a valid JWT")) throw new InvalidOperationException("Mismatched permission token was accepted.");
            admin.Secret = Jwt(4, -1); await model.Send.RunAsync();
            if (!model.Message.Contains("expired")) throw new InvalidOperationException("Expired role token was accepted.");
        }
    }
}
