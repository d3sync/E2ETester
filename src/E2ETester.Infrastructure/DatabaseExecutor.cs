using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace E2ETester.Infrastructure;

public sealed class DatabaseExecutor : IDatabaseExecutor
{
    public Task<QueryResult> ExecuteAsync(QueryDefinition query, DatabaseConnectionProfile profile, CancellationToken cancellationToken)
        => Task.Run(() => ExecuteCoreAsync(PackageJson.Clone(query), profile, cancellationToken));

    // A connectivity probe is not arbitrary SQL and does not assert account permissions.
    public Task<QueryResult> TestConnectionAsync(DatabaseConnectionProfile profile, CancellationToken callerToken)
        => Task.Run(async () =>
        {
            var started = DateTimeOffset.UtcNow;
            var clock = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await using var connection = CreateConnection(profile);
                await connection.OpenAsync(timeout.Token);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1"; command.CommandTimeout = 15;
                await command.ExecuteScalarAsync(timeout.Token);
                return new QueryResult { StartedAt = started, DurationMs = clock.ElapsedMilliseconds };
            }
            catch (OperationCanceledException) { return Failed(callerToken.IsCancellationRequested ? "cancelled" : "timeout", "Connection test stopped. Check the server address, port, VPN and firewall if it timed out."); }
            catch (DbException) when (timeout.IsCancellationRequested) { return Failed(callerToken.IsCancellationRequested ? "cancelled" : "timeout", "Connection test stopped. Check the server address, port, VPN and firewall if it timed out."); }
            catch (Exception ex) when (ex is DbException or ArgumentException or FormatException or InvalidOperationException or IOException or System.Security.Authentication.AuthenticationException)
            {
                return Failed("connection_failed", "Could not connect. Check server/port, database, credentials and TLS settings. " + DiagnosticCode(ex));
            }
            QueryResult Failed(string code, string error) => new() { StartedAt = started, DurationMs = clock.ElapsedMilliseconds, ErrorCode = code, Error = error };
        });

    // Return structured driver codes only; raw messages can contain credentials or SQL/data.
    private static string DiagnosticCode(Exception ex)
    {
        // Classify TLS locally; never echo provider messages containing host/user/data values.
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
            if (cause is System.Security.Authentication.AuthenticationException || cause.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || cause.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) || cause.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
                return "TLS negotiation failed. Check the server's TLS support, certificate and configured SSL mode.";
        return ex switch
    {
        MySqlException mysql => $"MySQL/MariaDB error {mysql.Number}, SQLSTATE {mysql.SqlState ?? "unknown"}.",
        SqlException sql => $"SQL Server error {sql.Number}.",
        PostgresException postgres => $"PostgreSQL SQLSTATE {postgres.SqlState}.",
        SqliteException sqlite => $"SQLite error {sqlite.SqliteErrorCode}.",
        System.Security.Authentication.AuthenticationException => "TLS authentication failed.",
        ArgumentException or FormatException => "The connection settings have an invalid format.",
        _ => "The driver could not complete the connection."
    };
    }

    public static DbConnection CreateConnection(DatabaseConnectionProfile profile) => profile.Provider switch
    {
        DatabaseProvider.Sqlite => new SqliteConnection(new SqliteConnectionStringBuilder(profile.ConnectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()),
        DatabaseProvider.SqlServer => new SqlConnection(profile.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(profile.ConnectionString),
        DatabaseProvider.PostgreSql => new NpgsqlConnection(profile.ConnectionString),
        _ => throw new InvalidDataException("Unsupported database provider.")
    };

    private static async Task<QueryResult> ExecuteCoreAsync(QueryDefinition query, DatabaseConnectionProfile profile, CancellationToken callerToken)
    {
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(query.TimeoutSeconds));
        var token = timeout.Token;
        var stage = "validating query settings";
        try
        {
            DatabaseQueries.Validate(query);
            if (profile.Provider != query.Provider || profile.Alias != query.ConnectionRef) throw new InvalidOperationException("Connection does not match the query.");
            if (profile.Provider != DatabaseProvider.Sqlite && !profile.ReadOnlyCredentialsConfirmed)
                return Failure("read_only_account_required", "Configure a database account restricted to reading, then confirm that access in the connection profile.");
            token.ThrowIfCancellationRequested();
            await using var connection = CreateConnection(profile);
            stage = "connecting to the database";
            await connection.OpenAsync(token);
            stage = "starting the read-only transaction";
            // Microsoft.Data.Sqlite's async APIs execute synchronously; interrupt the native VM for a real deadline.
            using var sqliteInterrupt = token.Register(() => { if (connection is SqliteConnection local && local.Handle is not null) SQLitePCL.raw.sqlite3_interrupt(local.Handle); });
            // Read-only accounts remain the enforcement boundary for server databases; transaction modes add protection.
            if (profile.Provider == DatabaseProvider.MySql) await SetupAsync(connection, "SET TRANSACTION READ ONLY", null, token);
            await using var transaction = connection is SqliteConnection sqlite ? sqlite.BeginTransaction(deferred: true) : await connection.BeginTransactionAsync(token);
            if (profile.Provider == DatabaseProvider.PostgreSql) await SetupAsync(connection, "SET TRANSACTION READ ONLY", transaction, token);
            if (profile.Provider == DatabaseProvider.Sqlite) await SetupAsync(connection, "PRAGMA query_only=ON", transaction, token);
            if (connection is SqliteConnection guarded)
            {
                SQLitePCL.raw.sqlite3_set_authorizer(guarded.Handle, (SQLitePCL.strdelegate_authorizer)((_, action, pragma, function, _, _) =>
                    action is SQLitePCL.raw.SQLITE_SELECT or SQLitePCL.raw.SQLITE_READ or SQLitePCL.raw.SQLITE_RECURSIVE or SQLitePCL.raw.SQLITE_TRANSACTION
                    || (action == SQLitePCL.raw.SQLITE_PRAGMA && pragma is "table_info" or "table_xinfo")
                    || (action == SQLitePCL.raw.SQLITE_FUNCTION && function is not ("load_extension" or "writefile" or "readfile"))
                        ? SQLitePCL.raw.SQLITE_OK : SQLitePCL.raw.SQLITE_DENY), null);
            }
            await using var command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandText = query.Sql; command.CommandTimeout = query.TimeoutSeconds;
            foreach (var parameter in query.Parameters)
            {
                var p = command.CreateParameter(); p.ParameterName = "@" + parameter.Name.TrimStart('@');
                (p.DbType, p.Value) = ParameterValue(parameter);
                command.Parameters.Add(p);
            }
            using var cancelRegistration = token.Register(() => { try { command.Cancel(); } catch (DbException) { } catch (ObjectDisposedException) { } });
            stage = "executing the query";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new DatabaseColumn(reader.GetName(i), reader.GetDataTypeName(i))).ToList();
            if (columns.Count == 0) return Failure("no_result_set", "The query did not return a table.");
            if (columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != columns.Count) return Failure("duplicate_columns", "Use unique column aliases so results can be referenced unambiguously.");
            var rows = new List<Dictionary<string, JsonElement>>();
            var truncated = false;
            var capturedChars = 0;
            while (await reader.ReadAsync(token))
            {
                if (rows.Count >= query.RowLimit) { truncated = true; break; }
                var row = new Dictionary<string, JsonElement>();
                for (var i = 0; i < columns.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    object? value;
                    if (await reader.IsDBNullAsync(i, token)) value = null;
                    else if (reader.GetFieldType(i) == typeof(string))
                    {
                        using var textReader = reader.GetTextReader(i);
                        var buffer = new char[16_385];
                        var length = await textReader.ReadBlockAsync(buffer.AsMemory(), token);
                        if (length > 16_384) truncated = true;
                        value = new string(buffer, 0, Math.Min(length, 16_384));
                    }
                    else if (reader.GetFieldType(i) == typeof(byte[]))
                    {
                        var buffer = new byte[16_385]; var length = reader.GetBytes(i, 0, buffer, 0, buffer.Length);
                        if (length > 16_384) truncated = true;
                        value = Convert.ToBase64String(buffer, 0, Math.Min((int)length, 16_384));
                    }
                    else value = reader.GetValue(i);
                    var cell = JsonSerializer.SerializeToElement(value);
                    capturedChars += cell.GetRawText().Length;
                    row.Add(columns[i].Name, cell);
                }
                rows.Add(row);
                if (capturedChars >= 2_000_000) { truncated = true; break; }
            }
            if (!truncated && await reader.NextResultAsync(token)) truncated = true;
            return new QueryResult { StartedAt = started, DurationMs = clock.ElapsedMilliseconds, Columns = columns, Rows = rows, Truncated = truncated };
            // Disposing the transaction rolls it back. No commit or API retry is performed.
        }
        catch (OperationCanceledException) { return Failure(callerToken.IsCancellationRequested ? "cancelled" : "timeout", "Database operation stopped; no request was retried."); }
        catch (DbException) when (timeout.IsCancellationRequested) { return Failure(callerToken.IsCancellationRequested ? "cancelled" : "timeout", "Database operation stopped; no request was retried."); }
        catch (Exception ex) when (ex is DbException or ArgumentException or FormatException or InvalidOperationException or IOException or OverflowException)
        {
            return Failure("database_error", $"Database operation failed while {stage}. {DiagnosticCode(ex)} Check connection settings, database permissions and SQL dialect.");
        }
        QueryResult Failure(string code, string error) => new() { StartedAt = started, DurationMs = clock.ElapsedMilliseconds, ErrorCode = code, Error = error };
    }
    private static async Task SetupAsync(DbConnection connection, string sql, DbTransaction? transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction; command.CommandTimeout = 10;
        await command.ExecuteNonQueryAsync(token);
    }
    public static (DbType Type, object Value) ParameterValue(QueryParameter p)
    {
        var type = p.Type switch { ParameterType.Int64 => DbType.Int64, ParameterType.Decimal => DbType.Decimal, ParameterType.Boolean => DbType.Boolean, ParameterType.Guid => DbType.Guid, ParameterType.DateTimeOffset => DbType.DateTimeOffset, _ => DbType.String };
        if (p.IsNull) return (type, DBNull.Value);
        object value = p.Type switch
        {
            ParameterType.Int64 => long.Parse(p.Value, CultureInfo.InvariantCulture),
            ParameterType.Decimal => decimal.Parse(p.Value, CultureInfo.InvariantCulture),
            ParameterType.Boolean => bool.Parse(p.Value),
            ParameterType.Guid => Guid.Parse(p.Value),
            ParameterType.DateTimeOffset => DateTimeOffset.Parse(p.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            _ => p.Value
        };
        return (type, value);
    }
}
