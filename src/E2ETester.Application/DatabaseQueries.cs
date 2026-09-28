using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2ETester.Core;

namespace E2ETester.Application;

public sealed class DatabaseQueries(IDatabaseExecutor executor)
{
    public static void Validate(QueryDefinition query)
    {
        if (string.IsNullOrWhiteSpace(query.Id) || string.IsNullOrWhiteSpace(query.ConnectionRef) || string.IsNullOrWhiteSpace(query.Sql)) throw new InvalidDataException("Query ID, connection reference and SQL are required.");
        if (query.Sql.Contains("{{", StringComparison.Ordinal)) throw new InvalidDataException("SQL text cannot contain bindings. Use named parameters such as @id.");
        if (query.RowLimit is < 1 or > 10000 || query.TimeoutSeconds is < 1 or > 600) throw new InvalidDataException("Query limits must be 1–10,000 rows and 1–600 seconds.");
        if (query.Parameters.Select(p => p.Name.TrimStart('@')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != query.Parameters.Count) throw new InvalidDataException("Query parameter names must be unique.");
        foreach (var p in query.Parameters)
            if (!System.Text.RegularExpressions.Regex.IsMatch(p.Name, "^@?[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidDataException("Use simple named SQL parameters, for example @userId.");
    }

    public static string Fingerprint(QueryDefinition query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PackageJson.Write(query))));

    public async Task<QueryEvidence> ExecuteAsync(QueryDefinition definition, DatabaseConnectionProfile profile, CancellationToken token, FixtureSelection? fixture = null, string? responseBody = null)
    {
        Validate(definition);
        if (profile.Alias != definition.ConnectionRef || profile.Provider != definition.Provider) throw new InvalidOperationException("Select the connection alias and provider required by this query.");
        var query = PackageJson.Clone(definition);
        foreach (var p in query.Parameters)
        {
            if (p.IsNull) continue;
            if (p.Value.StartsWith("{{", StringComparison.Ordinal))
            {
                var cell = ValueBindings.Resolve(p.Value, fixture, responseBody);
                p.IsNull = cell.ValueKind == JsonValueKind.Null;
                p.Value = ValueBindings.Text(cell);
            }
        }
        var result = await executor.ExecuteAsync(query, profile, token);
        return SafeEvidence(query, result, profile.ConnectionString);
    }

    public static QueryEvidence SafeEvidence(QueryDefinition query, QueryResult result, string connectionString = "")
    {
        return new QueryEvidence { Query = PackageJson.Clone(query), Result = PackageJson.Clone(result) };
    }

    public static CheckResult Assess(DatabaseCheck check, QueryEvidence evidence)
    {
        var result = evidence.Result;
        CheckResult VerdictResult(Verdict verdict, string actual, string message) => new("db:" + check.Id, check.Required, verdict, check.ExpectedJson, actual, message);
        if (result.ErrorCode is not null || result.Truncated) return VerdictResult(Verdict.Inconclusive, result.ErrorCode ?? "truncated", "Database evidence is incomplete.");
        if (check.Kind is DatabaseCheckKind.RowExists or DatabaseCheckKind.RowAbsent)
        {
            var exists = result.Rows.Count > 0;
            return VerdictResult(exists == (check.Kind == DatabaseCheckKind.RowExists) ? Verdict.Pass : Verdict.Fail, result.Rows.Count.ToString(CultureInfo.InvariantCulture), "Row existence check.");
        }
        JsonElement actual;
        if (check.Kind == DatabaseCheckKind.RowCount) actual = JsonSerializer.SerializeToElement(result.Rows.Count);
        else
        {
            if (result.Rows.Count != 1) return VerdictResult(Verdict.Fail, result.Rows.Count.ToString(CultureInfo.InvariantCulture), "Scalar check requires exactly one row.");
            if (!result.Rows[0].TryGetValue(check.Column, out actual)) return VerdictResult(Verdict.Inconclusive, "Missing column", $"Query did not return '{check.Column}'.");
        }
        using var expected = JsonDocument.Parse(check.ExpectedJson);
        var matches = JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(check.ExpectedJson));
        if (check.Kind == DatabaseCheckKind.ScalarNotEquals) matches = !matches;
        if (check.Kind is DatabaseCheckKind.ScalarGreaterThan or DatabaseCheckKind.ScalarLessThan)
        {
            if (actual.ValueKind != JsonValueKind.Number || !actual.TryGetDecimal(out var number) || !expected.RootElement.TryGetDecimal(out var target)) return VerdictResult(Verdict.Inconclusive, actual.GetRawText(), "Ordered comparison requires decimal numbers.");
            matches = check.Kind == DatabaseCheckKind.ScalarGreaterThan ? number > target : number < target;
        }
        return VerdictResult(matches ? Verdict.Pass : Verdict.Fail, actual.GetRawText(), matches ? "Database expectation matched." : "Database expectation did not match.");
    }

    public static FixtureSelection SelectFixture(QueryEvidence evidence, int rowIndex)
    {
        if (evidence.Result.ErrorCode is not null || evidence.Result.Truncated) throw new InvalidOperationException("Load a complete successful query result before choosing a fixture.");
        if (rowIndex < 0 || rowIndex >= evidence.Result.Rows.Count) throw new InvalidOperationException("Select a fixture row explicitly.");
        return new FixtureSelection(PackageJson.Clone(evidence), rowIndex);
    }
}
