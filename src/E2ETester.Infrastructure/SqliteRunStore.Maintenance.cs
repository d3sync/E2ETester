using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Infrastructure;
public sealed record LibraryEntry(string Id, string Title);
public sealed partial class SqliteRunStore
{
    public Task SavePackageAsync(TestPackage package, IEnumerable<string> secrets)
    {
        PackageJson.Validate(package);
        var safe = new Redaction(secrets.Concat(package.Cases.SelectMany(c => Redaction.Secrets(c.Request)))).Package(package);
        return Task.Run(() =>
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO packages(id,title,document) VALUES($id,$title,$doc)";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N")); command.Parameters.AddWithValue("$title", safe.Title); command.Parameters.AddWithValue("$doc", PackageJson.Write(safe)); command.ExecuteNonQuery();
        });
    }
    public Task<IReadOnlyList<LibraryEntry>> ListPackagesAsync() => Task.Run<IReadOnlyList<LibraryEntry>>(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT id,title FROM packages ORDER BY rowid DESC LIMIT 1000";
        using var reader = command.ExecuteReader(); var list = new List<LibraryEntry>(); while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1))); return list;
    });
    public Task<TestPackage?> LoadPackageAsync(string id) => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT document FROM packages WHERE id=$id"; command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string json ? PackageJson.Import(json) : null;
    });
    public Task BackupAsync(string destination) => Task.Run(() =>
    {
        var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) || File.Exists(destination)) throw new InvalidOperationException("Choose a new backup file outside the live journal.");
        using var connection = Open();
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        backup.Open(); connection.BackupDatabase(backup);
    });
    public Task DeleteFinishedAsync(string id) => Task.Run(() =>
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM runs WHERE id=$id AND json_extract(document,'$.finishedAt') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM workspace_draft WHERE json_extract(document,'$.runId')=$id)";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Only a finished run not linked to the current draft can be removed.");
        command.CommandText = "DELETE FROM agent_operations WHERE json_extract(document,'$.runId')=$id"; command.ExecuteNonQuery(); transaction.Commit();
    });
    public Task<string> DiagnosticsAsync() => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check"; var healthy = command.ExecuteScalar() as string == "ok";
        command.CommandText = "SELECT COUNT(*) FROM runs"; var count = command.ExecuteScalar();
        var directory = Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource)!;
        var bytes = Directory.EnumerateFiles(directory, "runs.db*").Sum(p => new FileInfo(p).Length);
        return PackageJson.Write(new { application = "E2E Tester", runtime = Environment.Version.ToString(), journalHealthy = healthy, runs = count, journalBytes = bytes, credentialsIncluded = false });
    });
}
