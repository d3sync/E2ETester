using System.Text.Json;

namespace E2ETester.Core;

public enum DatabaseProvider { Sqlite, SqlServer, MySql, PostgreSql }
public enum ParameterType { String, Int64, Decimal, Boolean, Guid, DateTimeOffset }
public enum DatabaseCheckKind { RowCount, ScalarEquals, ScalarNotEquals, ScalarGreaterThan, ScalarLessThan, RowExists, RowAbsent }

// Connection strings are local, memory-only inputs and must never enter portable packages or evidence.
public sealed record DatabaseConnectionProfile(string Alias, DatabaseProvider Provider, string ConnectionString, bool ReadOnlyCredentialsConfirmed = false);
public sealed record QueryParameter
{
    public string Name { get; set; } = "id";
    public ParameterType Type { get; set; } = ParameterType.String;
    public string Value { get; set; } = "";
    public bool IsNull { get; set; }
    public bool Sensitive { get; set; }
}
public sealed record QueryDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New query";
    public string ConnectionRef { get; set; } = "local-db";
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;
    public string Sql { get; set; } = "SELECT 1 AS value";
    public List<QueryParameter> Parameters { get; set; } = [];
    public List<string> SensitiveColumns { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
    public int RowLimit { get; set; } = 1000;
}
public sealed record DatabaseCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string QueryId { get; set; } = "";
    public DatabaseCheckKind Kind { get; set; } = DatabaseCheckKind.RowCount;
    public string Column { get; set; } = "";
    public string ExpectedJson { get; set; } = "1";
    public bool Required { get; set; } = true;
    public int PollTimeoutSeconds { get; set; }
    public int PollIntervalMilliseconds { get; set; } = 1000;
}
public sealed record DatabaseCheckPlan(DatabaseCheck Check, QueryDefinition Query);
public sealed record DatabaseColumn(string Name, string DataType);
public sealed record QueryResult
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public long DurationMs { get; init; }
    public List<DatabaseColumn> Columns { get; init; } = [];
    public List<Dictionary<string, JsonElement>> Rows { get; init; } = [];
    public bool Truncated { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}
public sealed record QueryEvidence
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public QueryDefinition Query { get; init; } = new();
    public QueryResult Result { get; init; } = new();
    public string? CheckId { get; init; }
    public CheckResult? Assessment { get; init; }
    public string Purpose { get; init; } = "exploration";
}
public sealed record FixtureSelection(QueryEvidence Evidence, int RowIndex);
