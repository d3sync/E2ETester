using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
namespace E2ETester.Desktop;
internal static class SessionAndOverrideSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "E2E-session-override", Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        var expiry = DateTimeOffset.UtcNow.AddHours(2);
        var jwt = "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":" + expiry.ToUnixTimeSeconds() + "}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test";
        using (var model = new MainViewModel(Path.Combine(root, "first"), sessions))
        {
            model.LoadNewSuite(); var current = model.CreateIdentity("permissions-user"); model.SelectedIdentity = current;
            current.Kind = AuthKind.Bearer; current.Secret = jwt;
            model.PreserveIdentityToken(current); current.Secret = "new-permissions-token";
            model.PreserveIdentityToken(current); model.PreserveIdentityToken(current);
            if (model.Identities.Count(i => i.IsSnapshot) != 2) throw new InvalidOperationException("Token snapshots were overwritten or duplicated.");
        }
        using (var model = new MainViewModel(Path.Combine(root, "second"), sessions))
        {
            model.LoadNewSuite();
            if (!model.Identities.Any(i => i.IsSnapshot && i.Secret == jwt) || !model.Identities.Any(i => i.IsSnapshot && i.Secret == "new-permissions-token")) throw new InvalidOperationException("Saved permission identities did not survive workspace restart.");
            var snapshot = model.Identities.Single(i => i.Secret == jwt);
            if (snapshot.ExpiresAt?.ToUnixTimeSeconds() != expiry.ToUnixTimeSeconds()) throw new InvalidOperationException("JWT expiry was not retained.");
        }
        var saved = new RememberedSessions(sessions);
        var fallback = saved.Read().Single(s => s.Secret == "new-permissions-token");
        if (saved.Read().Single(s => s.Secret == fallback.Secret).ExpiresAt != fallback.ExpiresAt || fallback.ExpiresAt != fallback.CapturedAt?.AddHours(24)) throw new InvalidOperationException("Opaque bearer retention extends on every read.");
        saved.Update(items => { for (var i = 0; i < items.Count; i++) items[i] = items[i] with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }; });
        if (saved.Read().Count != 0) throw new InvalidOperationException("Expired snapshots were retained.");
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var url = $"http://127.0.0.1:{number}";
        using var server = new HttpListener(); server.Prefixes.Add(url + "/"); server.Start();
        var source = new TestCase { Id = "source", Request = new() { Url = "/source" } };
        var target = new TestCase { Id = "target", Request = new() { Url = "/items/{{case.source./token}}" }, Assertions = [new()] };
        var package = new TestPackage { BaseUrl = url, Cases = [source, target] };
        var path = Path.Combine(root, "suite.jsee"); File.WriteAllText(path, PackageJson.Write(package));
        using var tester = new MainViewModel(Path.Combine(root, "override"), sessions);
        tester.OpenPackageFile(path); tester.SelectedCase = tester.Cases.Single(c => c.Baseline.Id == target.Id);
        tester.RunAnywayConfirmation = () => true;
        tester.ManualBindingInput = expressions => expressions.SequenceEqual(new[] { "{{case.source./token}}" }) ? new() { [expressions[0]] = JsonSerializer.SerializeToElement("verified-id") } : throw new InvalidOperationException("Missing binding prompt was incorrect.");
        tester.RunAnyway = true;
        var requestTask = server.GetContextAsync();
        var send = tester.Send.RunAsync();
        var received = await requestTask.WaitAsync(TimeSpan.FromSeconds(10));
        if (received.Request.RawUrl != "/items/verified-id") throw new InvalidOperationException("Manual override did not reach the API.");
        received.Response.StatusCode = 200; received.Response.Close(); await send;
        if (tester.SelectedAttempt?.Verdict != Verdict.Pass || tester.RunAnyway || !tester.SelectedAttempt.Changes.Any(c => c.Path == "execution.prerequisites") || !tester.SelectedAttempt.Changes.Any(c => c.Path == "url")) throw new InvalidOperationException("Run anyway was blocked or not audited: " + tester.Message);
    }
}
