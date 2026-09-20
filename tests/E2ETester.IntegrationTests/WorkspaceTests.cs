using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class WorkspaceTests
{
    public static async Task RunAsync(Action<bool, string> check, Func<Func<Task>, string, Task> reject, string baseUrl)
    {
        var directory = Path.Combine(Path.GetTempPath(), "E2ETester-workspace-tests", Guid.NewGuid().ToString("N"));
        using var store = new SqliteRunStore(directory);
        var source = new TestCase { Id = "source", Request = new() { Url = "/echo", Method = "POST", SendBody = true, Body = "{\"id\":42}" }, Assertions = [new()] };
        var consumer = new TestCase { Id = "consumer", Request = new() { Url = "/echo", Method = "POST", SendBody = true, Body = "{\"id\":\"{{case.source./id}}\"}" }, Assertions = [new() { Kind = CheckKind.JsonEquals, Selector = "/id", Expected = "42" }] };
        var package = new TestPackage { Cases = [source, consumer] };
        var coordinator = new RunCoordinator(store, new HttpTransport());
        var run = await coordinator.StartAsync(package, baseUrl);
        var identity = new IdentitySession("none", AuthKind.None);
        await reject(() => coordinator.ExecuteAsync(run, consumer, consumer.Request, consumer.Assertions, identity, default), "capture missing source blocks execution");
        var first = await coordinator.ExecuteAsync(run, source, source.Request, source.Assertions, identity, default);
        var second = await coordinator.ExecuteAsync(run, consumer, consumer.Request, consumer.Assertions, identity, default);
        check(second.Verdict == Verdict.Pass && second.CaptureSources["source"] == first.Id, "cross-case capture preserves JSON type and source attempt provenance");
        await coordinator.ExecuteAsync(run, source, source.Request, [new() { Expected = "403" }], identity, default);
        await reject(() => coordinator.ExecuteAsync(run, consumer, consumer.Request, consumer.Assertions, identity, default), "new failing source prevents reuse of stale successful capture");
        await reject(() => { PackageJson.Validate(package with { Cases = [consumer, source] }); return Task.CompletedTask; }, "dependency validation rejects reversed case order");
        await store.SavePackageAsync(package, []);
        var entries = await store.ListPackagesAsync();
        check((await store.LoadPackageAsync(entries[0].Id))?.Cases[1].Request.Body.Contains("{{case.source./id}}") == true, "package library preserves dependency templates");
        var draft = new WorkspaceDraft { QueryDraft = new() { Sql = "SELECT @password", Parameters = [new() { Name = "password", Value = "draft-query-canary" }] } };
        await store.SaveDraftAsync(draft, []);
        var recovered = await store.LoadDraftAsync();
        check(recovered?.QueryDraft?.Sql == "SELECT @password" && recovered.QueryDraft.Parameters[0].Value == Redaction.Mask, "unsaved Query Studio draft recovers with credential redaction");
        var backup = Path.Combine(directory, "backup.db"); await store.BackupAsync(backup);
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        { db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT count(*) FROM runs"; check((long)command.ExecuteScalar()! == 1, "SQLite online backup includes WAL-backed runs"); }
        await reject(() => store.DeleteFinishedAsync(run.Id), "retention cannot delete unfinished run");
        run.FinishedAt = DateTimeOffset.UtcNow; await store.SaveAsync(run);
        await store.SaveDraftAsync(draft with { RunId = run.Id }, []);
        await reject(() => store.DeleteFinishedAsync(run.Id), "retention protects run linked to current draft");
        var bundle = Path.Combine(directory, "report.zip"); await Reports.WriteBundleAsync(bundle, run);
        using (var zip = ZipFile.OpenRead(bundle))
        {
            using var manifestReader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
            using var manifest = JsonDocument.Parse(await manifestReader.ReadToEndAsync());
            var verified = true;
            foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
            { using var content = zip.GetEntry(file.GetProperty("path").GetString()!)!.Open(); using var data = new MemoryStream(); await content.CopyToAsync(data); verified &= Convert.ToHexString(SHA256.HashData(data.ToArray())) == file.GetProperty("sha256").GetString() && data.Length == file.GetProperty("bytes").GetInt64(); }
            check(verified && zip.Entries.Count == 4, "report bundle manifest verifies every exported artifact");
        }
        check(!(await store.DiagnosticsAsync()).Contains(baseUrl), "safe diagnostics exclude request targets and bodies");
        await store.SaveDraftAsync(draft, []); await store.DeleteFinishedAsync(run.Id);
        check(await store.LoadAsync(run.Id) is null, "explicit retention deletes eligible finished run");
        if (OperatingSystem.IsWindows())
        {
            var profiles = new ProtectedProfileStore(directory);
            await profiles.SaveAsync(new Dictionary<string, string> { ["token"] = "dpapi-canary-917" });
            check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "profiles.protected"))).Contains("dpapi-canary-917"), "Windows protected profile file excludes plaintext credential");
            check((await profiles.LoadAsync<Dictionary<string, string>>())?["token"] == "dpapi-canary-917", "Windows user protection restores local credentials");
            profiles.Forget(); check(await profiles.LoadAsync<Dictionary<string, string>>() is null, "forgetting profiles removes stored credentials");
        }
    }
}
