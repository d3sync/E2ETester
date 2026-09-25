using System.Data;
using System.Data.Common;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace E2ETester.Infrastructure;

public sealed class DatabasePreparationTarget(DatabaseConnectionProfile profile) : IPreparationTarget
{
    private DbConnection Connect(PreparationPlan plan)
    {
        Preparations.Validate(plan);
        if (profile.Alias != plan.ConnectionRef || profile.Provider != plan.Provider) throw new InvalidOperationException("Choose the connection named by the preparation.");
        if (profile.ReadOnlyCredentialsConfirmed) throw new InvalidOperationException("Preparation needs a write-enabled connection. Leave the read-only account flag off only if the account permits writes.");
        // Preparation is infrequent: prefer fresh sessions and sequential protocol commands over pooling/pipelining.
        // This also avoids reset-session incompatibilities with database proxies. TLS settings remain unchanged.
        if (profile.Provider == DatabaseProvider.MySql)
            return new MySqlConnection(new MySqlConnectionStringBuilder(profile.ConnectionString) { Pooling = false, Pipelining = false }.ToString());
        return profile.Provider == DatabaseProvider.Sqlite
            ? new SqliteConnection(new SqliteConnectionStringBuilder(profile.ConnectionString) { Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString())
            : DatabaseExecutor.CreateConnection(profile);
    }
    private string Quote(string name) => string.Join('.', name.Split('.').Select(part => profile.Provider switch { DatabaseProvider.MySql => "`" + part + "`", DatabaseProvider.SqlServer => "[" + part + "]", _ => "\"" + part + "\"" }));
    private static object Value(string json)
    {
        var value = Preparations.Scalar(json);
        return value.ValueKind switch { JsonValueKind.Null => DBNull.Value, JsonValueKind.String => value.GetString()!, JsonValueKind.True => true, JsonValueKind.False => false, _ => value.TryGetInt64(out var number) ? number : value.GetDecimal() };
    }
    private static void Add(DbCommand command, string name, string json) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = Value(json); command.Parameters.Add(p); }
    public async Task<string> ReadAsync(PreparationPlan plan, CancellationToken token)
    {
        var stage = "opening the database connection";
        try
        {
            await using var connection = Connect(plan); await connection.OpenAsync(token);
            stage = "reading the preparation target";
            return await Read(connection, null, plan, token);
        }
        catch (Exception ex) when (IsProtocolError(ex)) { throw ProtocolFailure(stage); }
    }
    private async Task<string> Read(DbConnection connection, DbTransaction? transaction, PreparationPlan plan, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 30;
        // MySQL runs the replace under REPEATABLE READ, so its in-transaction read takes the row lock explicitly.
        var lockRow = transaction is not null && profile.Provider == DatabaseProvider.MySql ? " FOR UPDATE" : "";
        command.CommandText = $"SELECT {Quote(plan.ValueColumn)} FROM {Quote(plan.Table)} WHERE {Quote(plan.KeyColumn)} = @key{lockRow}"; Add(command, "@key", plan.KeyJson);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Preparation target was not found.");
        var value = reader.IsDBNull(0) ? "null" : JsonSerializer.Serialize(reader.GetValue(0));
        Preparations.Scalar(value);
        if (await reader.ReadAsync(token)) throw new InvalidOperationException("Preparation key matched multiple rows. Use a unique key.");
        return value;
    }
    public async Task ReplaceAsync(PreparationPlan plan, string expectedJson, string replacementJson, CancellationToken token)
    {
        var stage = "opening the database connection";
        try
        {
            await using var connection = Connect(plan); await connection.OpenAsync(token);
            // Percona XtraDB Cluster with pxc_strict_mode=ENFORCING rejects SERIALIZABLE; the locking read plus the
            // conditional UPDATE below protect against overwriting an unexpected value; cluster conflicts can still occur.
            var isolation = profile.Provider == DatabaseProvider.MySql ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable;
            stage = "starting the preparation transaction";
            await using var transaction = await connection.BeginTransactionAsync(isolation, token);
            stage = "locking and checking the preparation target";
            var current = await Read(connection, transaction, plan, token);
            if (!Preparations.Equal(current, expectedJson)) throw new InvalidOperationException("Preparation target changed. No value was overwritten.");
            if (Preparations.Equal(current, replacementJson)) return;
            await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 30;
            command.CommandText = $"UPDATE {Quote(plan.Table)} SET {Quote(plan.ValueColumn)} = @value WHERE {Quote(plan.KeyColumn)} = @key AND ({Quote(plan.ValueColumn)} = @original OR ({Quote(plan.ValueColumn)} IS NULL AND @original IS NULL))";
            Add(command, "@value", replacementJson); Add(command, "@key", plan.KeyJson); Add(command, "@original", expectedJson);
            stage = "updating the preparation target";
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Preparation did not update exactly one row; transaction rolled back.");
            stage = "verifying the updated preparation target";
            if (!Preparations.Equal(await Read(connection, transaction, plan, token), replacementJson)) throw new InvalidOperationException("The database did not retain the expected value; transaction rolled back.");
            stage = "committing the preparation transaction";
            await transaction.CommitAsync(token);
            stage = "closing the preparation connection";
        }
        catch (Exception ex) when (IsProtocolError(ex)) { throw ProtocolFailure(stage); }
    }
    private static bool IsProtocolError(Exception error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause.GetType().Name == "MySqlProtocolException" || cause.Message.Contains("Packet received out-of-order", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private static InvalidOperationException ProtocolFailure(string stage) => new(
        $"MySQL protocol error while {stage}. Check database/proxy compatibility. No operation was retried. If recovery is pending, use Restore changes before preparing again; a write may have completed.");
}
