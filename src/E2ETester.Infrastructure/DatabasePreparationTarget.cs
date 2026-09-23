using System.Data;
using System.Data.Common;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Infrastructure;

public sealed class DatabasePreparationTarget(DatabaseConnectionProfile profile) : IPreparationTarget
{
    private DbConnection Connect(PreparationPlan plan)
    {
        Preparations.Validate(plan);
        if (profile.Alias != plan.ConnectionRef || profile.Provider != plan.Provider) throw new InvalidOperationException("Choose the connection named by the preparation.");
        if (profile.ReadOnlyCredentialsConfirmed) throw new InvalidOperationException("Preparation needs a separate write-enabled connection, not a read-only Query Studio account.");
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
        await using var connection = Connect(plan); await connection.OpenAsync(token);
        return await Read(connection, null, plan, token);
    }
    private async Task<string> Read(DbConnection connection, DbTransaction? transaction, PreparationPlan plan, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 30;
        command.CommandText = $"SELECT {Quote(plan.ValueColumn)} FROM {Quote(plan.Table)} WHERE {Quote(plan.KeyColumn)} = @key"; Add(command, "@key", plan.KeyJson);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Preparation target was not found.");
        var value = reader.IsDBNull(0) ? "null" : JsonSerializer.Serialize(reader.GetValue(0));
        Preparations.Scalar(value);
        if (await reader.ReadAsync(token)) throw new InvalidOperationException("Preparation key matched multiple rows. Use a unique key.");
        return value;
    }
    public async Task ReplaceAsync(PreparationPlan plan, string expectedJson, string replacementJson, CancellationToken token)
    {
        await using var connection = Connect(plan); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var current = await Read(connection, transaction, plan, token);
        if (!Preparations.Equal(current, expectedJson)) throw new InvalidOperationException("Preparation target changed. No value was overwritten.");
        if (Preparations.Equal(current, replacementJson)) return;
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 30;
        command.CommandText = $"UPDATE {Quote(plan.Table)} SET {Quote(plan.ValueColumn)} = @value WHERE {Quote(plan.KeyColumn)} = @key AND ({Quote(plan.ValueColumn)} = @original OR ({Quote(plan.ValueColumn)} IS NULL AND @original IS NULL))";
        Add(command, "@value", replacementJson); Add(command, "@key", plan.KeyJson); Add(command, "@original", expectedJson);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Preparation did not update exactly one row; transaction rolled back.");
        if (!Preparations.Equal(await Read(connection, transaction, plan, token), replacementJson)) throw new InvalidOperationException("The database did not retain the expected value; transaction rolled back.");
        await transaction.CommitAsync(token);
    }
}
