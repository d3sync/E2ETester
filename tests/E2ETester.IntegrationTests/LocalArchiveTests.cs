using System.IO.Compression;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class LocalArchiveTests
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var suite = SuiteDefaults.Create(); suite.SchemaVersion = "1.6";
        suite.Cases.Add(new() { Id = "role", Request = new() { IdentityRef = "reader" }, Preparation = new() { Table = "accounts", RequireFreshLogin = true, JwtClaim = "permissions", Flags = [new("Read", 4)] } });
        var actions = PermissionActions.ForPackage(suite);
        check(actions.Any(a => a.Title == "Check API access") && actions.Any(a => a.Title == "Choose permissions…") && actions.Any(a => a.Title == "Set permissions: reader"), "Permission suites expose access, role and manual actions outside coverage");
        PackageJson.Validate(suite with { GuidedActions = actions });
        check(PermissionActions.ForPackage(suite with { GuidedActions = actions }).Count == actions.Count, "Convenience actions do not accumulate duplicates");
        var run = new TestRun { Package = suite, Attempts = [new() { CaseId = "role", State = AttemptState.Completed, Verdict = Verdict.Fail, Response = new() { Body = "<script>unsafe()</script>" + new string('x', 20000), StatusCode = 500 } }] };
        var stamp = DateTimeOffset.Now;
        var pair = await Reports.WritePairAsync(Path.Combine(directory, "Reports"), "customer-suite.jsee", run, stamp);
        var html = await File.ReadAllTextAsync(pair.Simple);
        check(Path.GetFileName(pair.Simple).StartsWith("customer-suite_report") && pair.Full.EndsWith("_full.zip"), "Paired report names use source package and timestamp");
        check(html.Contains("NOT RUN") && html.Contains("FAIL") && html.Contains("Preview shortened") && !html.Contains("<script>"), "Simple report preserves incomplete/failed outcomes and safely bounds response previews");
        using (var zip = ZipFile.OpenRead(pair.Full))
        {
            using var reader = new StreamReader(zip.GetEntry("report.json")!.Open());
            check((await reader.ReadToEndAsync()).Contains(new string('x', 20000)) && zip.GetEntry("manifest.json") is not null, "Full ZIP retains complete evidence and integrity manifest");
        }
        var second = await Reports.WritePairAsync(Path.Combine(directory, "Reports"), "customer-suite.jsee", run, stamp);
        check(second.Full != pair.Full && File.Exists(pair.Full), "Timestamp collisions preserve both report pairs");
        if (OperatingSystem.IsWindows())
        {
            var location = Path.Combine(directory, "Connections"); var saved = new SavedConnections(location);
            var profile = new DatabaseConnectionProfile("db", DatabaseProvider.MySql, "Server=localhost;User ID=test;Password=archive-secret-canary", false);
            saved.Save("https://api.example.com", profile, false);
            check(new SavedConnections(location).Read("https://api.example.com").Single() == profile, "Remembered connections decrypt after restart for the Windows user");
            check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(location).Single())).Contains("archive-secret-canary"), "Connection credentials are not stored as plaintext");
            check(saved.Read("https://other.example.com").Count == 0, "Saved connections are isolated by API origin");
            saved.Save("https://api.example.com", profile, true);
            check(saved.Read("https://api.example.com").Count == 0, "Forgetting a connection removes its remembered credentials");
        }
    }
}
