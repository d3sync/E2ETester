using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Infrastructure;

public sealed partial class SqliteRunStore : IRunStore, IRunHistoryStore, IDisposable
{
    private readonly string connectionString;
    private readonly FileStream workspaceLock;

    public SqliteRunStore(string directory)
    {
        Directory.CreateDirectory(directory);
        // An OS file handle, not an in-process lock, prevents two hosts from owning this workspace.
        workspaceLock = new FileStream(Path.Combine(directory, "workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "runs.db") }.ToString();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, started TEXT NOT NULL, document TEXT NOT NULL); CREATE TABLE IF NOT EXISTS workspace_draft (id INTEGER PRIMARY KEY CHECK(id=1), document TEXT NOT NULL); CREATE TABLE IF NOT EXISTS agent_operations (id TEXT PRIMARY KEY, document TEXT NOT NULL);";
            command.ExecuteNonQuery();
            command.CommandText = "CREATE TABLE IF NOT EXISTS packages (id TEXT PRIMARY KEY, title TEXT NOT NULL, document TEXT NOT NULL);"; command.ExecuteNonQuery();
        }
        catch { workspaceLock.Dispose(); throw; }
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }
    public Task SaveAsync(TestRun run)
    {
        var json = PackageJson.Write(run);
        return Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO runs(id, started, document) VALUES($id,$started,$json) ON CONFLICT(id) DO UPDATE SET document=$json;";
            command.Parameters.AddWithValue("$id", run.Id); command.Parameters.AddWithValue("$started", run.StartedAt.ToString("O")); command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        });
    }
    public Task<TestRun?> LoadLatestAsync() => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM runs ORDER BY started DESC, id DESC LIMIT 1;";
        return command.ExecuteScalar() is string json ? PackageJson.Read<TestRun>(json) : null;
    });
    public Task<TestRun?> LoadAsync(string id) => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM runs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string json ? PackageJson.Read<TestRun>(json) : null;
    });

    public Task<IReadOnlyList<RunSummary>> ListAsync(int offset = 0, int limit = 50)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit), "Use a nonnegative offset and 1–100 rows.");
        return Task.Run<IReadOnlyList<RunSummary>>(() =>
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, json_extract(document, '$.package.title'), json_extract(document, '$.baseUrl'), started, json_extract(document, '$.finishedAt'), json_array_length(document, '$.attempts') FROM runs ORDER BY started DESC, id DESC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            var rows = new List<RunSummary>();
            while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture), reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(5)));
            return rows;
        });
    }
    public void Dispose() => workspaceLock.Dispose();
    public Task SaveOperationAsync(string id, string document) => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO agent_operations(id,document) VALUES($id,$document) ON CONFLICT(id) DO UPDATE SET document=$document;";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$document", document); command.ExecuteNonQuery();
    });
    public Task<string?> LoadOperationAsync(string id) => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM agent_operations WHERE id=$id;"; command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    });
    public Task SaveDraftAsync(WorkspaceDraft draft, IEnumerable<string> secrets)
    {
        var json = PackageJson.Write(draft.Redacted(secrets));
        return Task.Run(() =>
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO workspace_draft(id,document) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET document=$json;";
            command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
        });
    }
    public Task<WorkspaceDraft?> LoadDraftAsync() => Task.Run(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM workspace_draft WHERE id=1;";
        return command.ExecuteScalar() is string json ? PackageJson.Read<WorkspaceDraft>(json) : null;
    });
}
