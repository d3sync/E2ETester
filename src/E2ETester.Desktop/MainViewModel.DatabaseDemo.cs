using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;
using System.IO;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    public AsyncCommand LoadDatabaseDemo { get; private set; } = null!;
    private async Task LoadDatabaseDemoAsync(string workspace)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(workspace, "demo-fixtures.db") }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS demo_fixture (id INTEGER PRIMARY KEY, name TEXT NOT NULL, enabled INTEGER NOT NULL); INSERT OR IGNORE INTO demo_fixture VALUES (1, 'Example fixture', 1), (2, 'Read-only fixture', 0);";
            await command.ExecuteNonQueryAsync();
        }
        LoadPackage(DatabaseDemoPackage());
        var profile = Database.Connections.FirstOrDefault(c => c.Alias == "demo-sqlite");
        if (profile is null) { profile = new ConnectionEditor { Alias = "demo-sqlite" }; Database.Connections.Add(profile); }
        profile.Provider = DatabaseProvider.Sqlite; profile.ConnectionString = connectionString;
        Database.SelectedQuery = Database.Queries[0]; Database.SelectedConnection = profile;
        Message = "Database demo ready. Open Query Studio, run the fixture query, select the first row, and use it. Then send the API call and verify the database.";
    }

    public static TestPackage DatabaseDemoPackage() => new()
    {
        SchemaVersion = "1.1", Id = "database-demo", Title = "Database-backed API test", BaseUrl = "http://localhost:5188",
        Description = "Read a local SQLite fixture, echo it through the demo API, then verify the response ID against the database.",
        Queries =
        [
            new() { Id = "demo-fixtures", Title = "Choose a demo fixture", ConnectionRef = "demo-sqlite", Sql = "SELECT id, name, enabled FROM demo_fixture ORDER BY id" },
            new() { Id = "demo-verify", Title = "Verify returned ID in database", ConnectionRef = "demo-sqlite", Sql = "SELECT name FROM demo_fixture WHERE id = @id", Parameters = [new() { Name = "id", Type = ParameterType.Int64, Value = "{{response./id}}" }] }
        ],
        Cases =
        [
            new()
            {
                Id = "db-round-trip", Title = "01 · Verify an API result against the database", FixtureQueryId = "demo-fixtures",
                Instructions = "Select the first fixture row in Query Studio. Send the request, then run Verify database. HTTP success alone remains inconclusive until the database check passes.",
                Request = new() { Method = "POST", Url = "/echo", SendBody = true, Body = "{\n  \"id\": \"{{fixture.id}}\",\n  \"name\": \"{{fixture.name}}\"\n}" },
                Assertions = [new()],
                DatabaseChecks = [new() { Id = "verify-name", QueryId = "demo-verify", Kind = DatabaseCheckKind.ScalarEquals, Column = "name", ExpectedJson = "\"Example fixture\"" }]
            }
        ]
    };
}
