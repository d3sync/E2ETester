using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

internal static class LiveDatabaseTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var password = Environment.GetEnvironmentVariable("E2E_DATABASE_PASSWORD") ?? throw new InvalidOperationException("Run Test-Databases.ps1 to create disposable infrastructure.");
        foreach (var provider in new[] { DatabaseProvider.SqlServer, DatabaseProvider.MySql, DatabaseProvider.PostgreSql })
        {
            Console.WriteLine($"Preparing isolated {provider} fixtures...");
            var admin = Profile(provider, true, password);
            await using (var connection = DatabaseExecutor.CreateConnection(admin))
            {
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    try { await connection.OpenAsync(); break; }
                    catch (DbException) when (deadline.Elapsed < TimeSpan.FromMinutes(3)) { await Task.Delay(1000); }
                }
                var seed = provider switch
                {
                    DatabaseProvider.SqlServer => $"CREATE DATABASE fixtures; CREATE LOGIN fixture_reader WITH PASSWORD = '{password}';",
                    DatabaseProvider.MySql => $"CREATE DATABASE fixtures; CREATE USER 'fixture_reader'@'%' IDENTIFIED BY '{password}'; GRANT SELECT ON fixtures.* TO 'fixture_reader'@'%';",
                    _ => $"CREATE ROLE fixture_reader LOGIN PASSWORD '{password}';"
                };
                await Execute(connection, seed);
                if (provider == DatabaseProvider.PostgreSql) await Execute(connection, "CREATE DATABASE fixtures");
                await connection.ChangeDatabaseAsync("fixtures");
                await Execute(connection, "CREATE TABLE fixture (id INTEGER PRIMARY KEY, name VARCHAR(100), note VARCHAR(100), password VARCHAR(100)); INSERT INTO fixture VALUES (1, 'Example fixture', NULL, 'live-db-canary'), (2, 'Second fixture', '', 'other-canary');");
                if (provider == DatabaseProvider.SqlServer) await Execute(connection, "CREATE USER fixture_reader FOR LOGIN fixture_reader; GRANT SELECT ON dbo.fixture TO fixture_reader;");
                if (provider == DatabaseProvider.PostgreSql) await Execute(connection, "GRANT CONNECT ON DATABASE fixtures TO fixture_reader; GRANT USAGE ON SCHEMA public TO fixture_reader; GRANT SELECT ON fixture TO fixture_reader;");
            }
            var profile = Profile(provider, false, password);
            var adminBuilder = new DbConnectionStringBuilder { ConnectionString = admin.ConnectionString };
            adminBuilder[provider == DatabaseProvider.SqlServer ? "Initial Catalog" : "Database"] = "fixtures";
            var writable = admin with { ConnectionString = adminBuilder.ConnectionString, ReadOnlyCredentialsConfirmed = false };
            check((await new DatabaseExecutor().TestConnectionAsync(writable, default)).ErrorCode is null, $"{provider}: connectivity test accepts write-enabled preparation profile");
            check((await new DatabaseExecutor().TestConnectionAsync(profile, default)).ErrorCode is null, $"{provider}: connectivity test accepts read-only query profile");
            if (provider == DatabaseProvider.MySql)
            {
                var details = DatabaseConnectionDetails.Read(writable);
                details.Details.MySqlTlsMode = MySqlSslMode.Required;
                var tlsProbe = await new DatabaseExecutor().TestConnectionAsync(writable with { ConnectionString = details.Details.Build(details.Password, writable.ConnectionString) }, default);
                check(tlsProbe.Error is null || tlsProbe.Error.Contains("TLS negotiation failed"), "MySQL/MariaDB: required TLS either connects or gives a specific TLS diagnostic");
                Console.WriteLine(tlsProbe.Error is null ? "Required TLS supported by test server." : "Required TLS probe: " + tlsProbe.Error);
                var invalidPassword = new MySqlConnectionStringBuilder(writable.ConnectionString) { Password = "invalid-test-password" };
                var rejectedLogin = await new DatabaseExecutor().TestConnectionAsync(writable with { ConnectionString = invalidPassword.ToString() }, default);
                check(rejectedLogin.ErrorCode == "connection_failed" && rejectedLogin.Error!.Contains("1045") && !rejectedLogin.Error.Contains("invalid-test-password"), "MySQL/MariaDB: bad login exposes error code without credentials");
            }
            var preparation = new PreparationPlan { Provider = provider, ConnectionRef = "live-db", Table = "fixture", KeyColumn = "id", KeyJson = "1", ValueColumn = "note", ValueJson = "\"prepared\"" };
            var target = new DatabasePreparationTarget(writable);
            var recovery = new PreparationRecovery { Plan = preparation, OriginalJson = await target.ReadAsync(preparation, default) };
            await Preparations.ApplyAsync(recovery, target, () => Task.CompletedTask, default);
            check(await target.ReadAsync(preparation, default) == "\"prepared\"", $"{provider}: preparation writes a single target row");
            await Preparations.RestoreAsync(recovery, target, () => Task.CompletedTask, default);
            check(await target.ReadAsync(preparation, default) == "null", $"{provider}: preparation restores the original NULL");
            var queries = new DatabaseQueries(new DatabaseExecutor());
            var query = new QueryDefinition { Id = "live-fixtures", ConnectionRef = "live-db", Provider = provider, Sql = "SELECT id, name, note, password FROM fixture ORDER BY id" };
            void Verify(bool condition, string name) => check(condition, $"{provider}: {name}");
            var evidence = await queries.ExecuteAsync(query, profile, default);
            Verify(evidence.Result.ErrorCode is null && evidence.Result.Rows.Count == 2, "restricted account reads fixtures");
            Verify(evidence.Result.Rows[0]["note"].ValueKind == JsonValueKind.Null && evidence.Result.Rows[1]["note"].GetString() == "", "null and empty stay distinct");
            Verify(!PackageJson.Write(evidence).Contains("live-db-canary") && !PackageJson.Write(evidence).Contains(password), "evidence excludes credentials and secret columns");
            var selected = DatabaseQueries.SelectFixture(evidence, 0);
            Verify(ValueBindings.Request(new RequestDefinition { Url = "/items/{{fixture.id}}" }, selected).Url == "/items/1", "fixture binds into API request");
            var bound = query with { Sql = "SELECT name FROM fixture WHERE id = @id", Parameters = [new() { Name = "id", Type = ParameterType.Int64, Value = "{{response./id}}" }] };
            var result = await queries.ExecuteAsync(bound, profile, default, responseBody: "{\"id\":1}");
            Verify(DatabaseQueries.Assess(new DatabaseCheck { Kind = DatabaseCheckKind.ScalarEquals, Column = "name", ExpectedJson = "\"Example fixture\"" }, result).Verdict == Verdict.Pass, "API response binds into scalar verification");
            var injection = await queries.ExecuteAsync(query with { Sql = "SELECT id FROM fixture WHERE name = @name", Parameters = [new() { Name = "name", Value = "' OR 1=1 --" }] }, profile, default);
            Verify(injection.Result.ErrorCode is null && injection.Result.Rows.Count == 0, "parameters resist SQL injection");
            foreach (var (type, value) in new[] { (ParameterType.Int64, "42"), (ParameterType.Decimal, "12.34"), (ParameterType.Boolean, "true"), (ParameterType.Guid, "1c12a940-c12c-4c43-bdc8-2e503b7ad801"), (ParameterType.DateTimeOffset, "2026-09-20T12:30:00+03:00"), (ParameterType.String, "Καλημέρα") })
            {
                var typed = query with { Sql = "SELECT @value AS result", Parameters = [new() { Name = "value", Type = type, Value = value }] };
                var actual = await queries.ExecuteAsync(typed, profile, default);
                Verify(actual.Result.ErrorCode is null && actual.Result.Rows.Count == 1, $"{type} parameter executes");
                var cell = actual.Result.Rows[0]["result"];
                Verify(type switch
                {
                    ParameterType.Int64 => cell.GetInt64() == 42,
                    ParameterType.Decimal => cell.GetDecimal() == 12.34m,
                    ParameterType.Boolean => cell.ToString() is "True" or "true" or "1",
                    ParameterType.Guid => Guid.Parse(cell.GetString()!) == Guid.Parse(value),
                    ParameterType.DateTimeOffset => DateTimeOffset.Parse(cell.GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal).UtcDateTime == DateTimeOffset.Parse(value).UtcDateTime,
                    _ => cell.GetString() == value
                }, $"{type} parameter preserves value");
                typed.Parameters[0].IsNull = true;
                var nullable = await queries.ExecuteAsync(typed, profile, default);
                Verify(nullable.Result.ErrorCode is null && nullable.Result.Rows[0]["result"].ValueKind == JsonValueKind.Null, $"{type} typed NULL executes");
            }
            var capped = await queries.ExecuteAsync(query with { RowLimit = 1 }, profile, default);
            Verify(capped.Result.Truncated && DatabaseQueries.Assess(new(), capped).Verdict == Verdict.Inconclusive, "row limit cannot produce PASS");
            var large = await queries.ExecuteAsync(query with { Sql = provider == DatabaseProvider.SqlServer ? "SELECT REPLICATE(CAST('x' AS VARCHAR(MAX)), 20000) AS text" : "SELECT REPEAT('x', 20000) AS text" }, profile, default);
            Verify(large.Result.ErrorCode is null && large.Result.Truncated && large.Result.Rows[0]["text"].GetString()!.Length == 16384, "large cell is bounded");
            var duplicate = await queries.ExecuteAsync(query with { Sql = "SELECT 1 AS id, 2 AS id" }, profile, default);
            Verify(duplicate.Result.ErrorCode == "duplicate_columns", "ambiguous columns rejected");
            // Test the actual account privileges independently of the executor's read-only transaction.
            await using (var restricted = DatabaseExecutor.CreateConnection(profile))
            {
                await restricted.OpenAsync();
                var denied = false;
                try { await Execute(restricted, "DELETE FROM fixture"); } catch (DbException) { denied = true; }
                Verify(denied, "database account itself denies writes");
            }
            var write = await queries.ExecuteAsync(query with { Sql = "DELETE FROM fixture" }, profile, default);
            Verify(write.Result.ErrorCode == "database_error", "Query Studio write attempt fails safely");
            var slowSql = provider switch { DatabaseProvider.SqlServer => "WAITFOR DELAY '00:00:20'; SELECT 1 AS result", DatabaseProvider.MySql => "SELECT SLEEP(20) AS result", _ => "SELECT pg_sleep(20)" };
            var slow = query with { Sql = slowSql, TimeoutSeconds = 1 };
            var clock = Stopwatch.StartNew();
            var timeout = await queries.ExecuteAsync(slow, profile, default);
            Verify(timeout.Result.ErrorCode == "timeout" && clock.Elapsed < TimeSpan.FromSeconds(10), "active query deadline stops work");
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                var cancelled = await queries.ExecuteAsync(slow with { TimeoutSeconds = 30 }, profile, cancellation.Token);
                Verify(cancelled.Result.ErrorCode == "cancelled", "active query cancellation is recorded");
            }
            var recovered = await queries.ExecuteAsync(query, profile, default);
            Verify(recovered.Result.ErrorCode is null && recovered.Result.Rows.Count == 2, "connections recover and fixtures remain intact");
        }
    }

    private static DatabaseConnectionProfile Profile(DatabaseProvider provider, bool admin, string password)
    {
        // Inputs are generated by the isolated Docker launcher, never a user's saved connection profile.
        if (!System.Text.RegularExpressions.Regex.IsMatch(password, "^E2e![a-f0-9]{32}$")) throw new InvalidOperationException("Expected a disposable test credential.");
        var portName = provider switch { DatabaseProvider.SqlServer => "SQLSERVER", DatabaseProvider.MySql => "MYSQL", _ => "POSTGRES" };
        var port = int.Parse(Environment.GetEnvironmentVariable($"E2E_{portName}_PORT") ?? throw new InvalidOperationException("Missing disposable database port."));
        var database = admin ? provider == DatabaseProvider.SqlServer ? "master" : provider == DatabaseProvider.MySql ? "mysql" : "postgres" : "fixtures";
        var user = admin ? provider == DatabaseProvider.SqlServer ? "sa" : provider == DatabaseProvider.MySql ? "root" : "postgres" : "fixture_reader";
        var connectionString = provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnectionStringBuilder { DataSource = $"127.0.0.1,{port}", InitialCatalog = database, UserID = user, Password = password, TrustServerCertificate = true, ConnectTimeout = 3 }.ToString(),
            DatabaseProvider.MySql => new MySqlConnectionStringBuilder { Server = "127.0.0.1", Port = (uint)port, Database = database, UserID = user, Password = password, ConnectionTimeout = 3 }.ToString(),
            _ => new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = port, Database = database, Username = user, Password = password, Timeout = 3 }.ToString()
        };
        return new("live-db", provider, connectionString, true);
    }

    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
