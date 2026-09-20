using System.Text.Json;
using System.Text.Json.Nodes;
using E2ETester.Core;

namespace E2ETester.Application;

public static class Assertions
{
    public static List<CheckResult> Evaluate(IEnumerable<Assertion> assertions, ResponseSnapshot response) => assertions.Select(a => Evaluate(a, response)).ToList();

    private static CheckResult Evaluate(Assertion a, ResponseSnapshot r)
    {
        CheckResult Result(Verdict verdict, string actual, string message) => new(a.Id, a.Required, verdict, a.Expected, actual, message);
        if (r.StatusCode is null) return Result(Verdict.Inconclusive, "No response", "No HTTP response was available.");
        var bodyCheck = a.Kind is CheckKind.BodyContains or CheckKind.BodyNotEmpty or CheckKind.JsonEquals or CheckKind.JsonExists or CheckKind.JsonGreaterThan or CheckKind.JsonLessThan or CheckKind.JsonArrayCount;
        if (r.BodyEncoding != "text" && bodyCheck) return Result(Verdict.Inconclusive, "Binary body", "Text checks cannot evaluate a binary response.");
        if (bodyCheck && (r.Truncated || r.ErrorCode is not null))
            return Result(Verdict.Inconclusive, "Incomplete body", "The full response body was not captured.");
        string actual;
        bool passed;
        try
        {
            switch (a.Kind)
            {
                case CheckKind.Status:
                    actual = r.StatusCode.ToString()!;
                    passed = actual == a.Expected;
                    break;
                case CheckKind.StatusIn:
                    actual = r.StatusCode.ToString()!; using (var statuses = JsonDocument.Parse(a.Expected)) passed = statuses.RootElement.EnumerateArray().Any(s => s.GetInt32() == r.StatusCode); break;
                case CheckKind.DurationLessThan:
                    actual = r.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture); passed = r.DurationMs < long.Parse(a.Expected, System.Globalization.CultureInfo.InvariantCulture); break;
                case CheckKind.HeaderExists:
                    passed = r.Headers.Keys.Any(h => h.Equals(a.Selector, StringComparison.OrdinalIgnoreCase)); actual = passed ? "present" : "<missing>"; break;
                case CheckKind.BodyNotEmpty:
                    actual = r.Body;
                    passed = !string.IsNullOrWhiteSpace(actual);
                    break;
                case CheckKind.BodyContains:
                    actual = r.Body;
                    passed = actual.Contains(a.Expected, StringComparison.Ordinal);
                    break;
                case CheckKind.HeaderEquals:
                    var pair = r.Headers.FirstOrDefault(h => h.Key.Equals(a.Selector, StringComparison.OrdinalIgnoreCase));
                    actual = pair.Value is null ? "<missing>" : string.Join(", ", pair.Value);
                    passed = actual == a.Expected;
                    break;
                case CheckKind.JsonEquals:
                case CheckKind.JsonExists:
                case CheckKind.JsonGreaterThan:
                case CheckKind.JsonLessThan:
                case CheckKind.JsonArrayCount:
                    using (var json = JsonDocument.Parse(r.Body))
                    {
                        var node = json.RootElement;
                        foreach (var segment in a.Selector.Split('/').Skip(1))
                        {
                            var key = segment.Replace("~1", "/").Replace("~0", "~");
                            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var child)) node = child;
                            else if (node.ValueKind == JsonValueKind.Array && int.TryParse(key, out var index) && index >= 0 && index < node.GetArrayLength()) node = node[index];
                            else return Result(Verdict.Fail, "<missing>", $"JSON path {a.Selector} was not found.");
                        }
                        actual = node.GetRawText();
                        if (a.Kind == CheckKind.JsonExists) passed = true;
                        else if (a.Kind == CheckKind.JsonArrayCount)
                        { passed = node.ValueKind == JsonValueKind.Array && node.GetArrayLength() == int.Parse(a.Expected, System.Globalization.CultureInfo.InvariantCulture); actual = node.ValueKind == JsonValueKind.Array ? node.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture) : "not an array"; }
                        else if (a.Kind is CheckKind.JsonGreaterThan or CheckKind.JsonLessThan)
                        { var target = decimal.Parse(a.Expected, System.Globalization.CultureInfo.InvariantCulture); passed = node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var number) && (a.Kind == CheckKind.JsonGreaterThan ? number > target : number < target); }
                        else passed = JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(a.Expected));
                    }
                    break;
                default: return Result(Verdict.Inconclusive, "Unsupported", "Unsupported assertion.");
            }
            return Result(passed ? Verdict.Pass : Verdict.Fail, actual, $"{a.Kind}{(a.Selector.Length > 0 ? " " + a.Selector : "")}: {(passed ? "matched" : "did not match")}");
        }
        catch (JsonException) { return Result(Verdict.Fail, "Invalid JSON", "The response was not valid JSON."); }
    }
}
