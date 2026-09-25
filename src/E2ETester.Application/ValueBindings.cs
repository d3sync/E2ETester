using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using E2ETester.Core;

namespace E2ETester.Application;

public static class ValueBindings
{
    private static readonly Regex Marker = new(@"\{\{([^{}]+)\}\}", RegexOptions.CultureInvariant);
    public static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    public static JsonElement Resolve(string expression, FixtureSelection? fixture, string? responseBody = null, TestRun? run = null, Dictionary<string, string>? sources = null, IReadOnlyDictionary<string, string>? selectedAttempts = null)
    {
        var match = Marker.Match(expression);
        if (!match.Success || match.Length != expression.Length) throw new InvalidDataException("A typed binding must be a complete {{fixture.column}} or {{response./json/pointer}} expression.");
        var source = match.Groups[1].Value;
        if (source.StartsWith("case.", StringComparison.Ordinal))
        {
            var separator = source.IndexOf('.', 5);
            if (separator < 0) throw new InvalidDataException("Use {{case.case-id./json/pointer}}.");
            var id = source[5..separator];
            var explicitlySelected = selectedAttempts is not null && selectedAttempts.TryGetValue(id, out _);
            var attempt = explicitlySelected ? run?.Attempts.SingleOrDefault(a => a.CaseId == id && a.Id == selectedAttempts![id]) : run?.Attempts.LastOrDefault(a => a.CaseId == id);
            if (attempt is not { State: AttemptState.Completed, Response: { Truncated: false, ErrorCode: null, BodyEncoding: "text" } } || (!explicitlySelected && attempt.Verdict != Verdict.Pass)) throw new InvalidOperationException($"Run source case '{id}' successfully before using its response.");
            if (sources is not null) sources[id] = attempt.Id;
            return Resolve("{{response." + source[(separator + 1)..] + "}}", null, attempt.Response.Body);
        }
        JsonElement cell;
        if (source.StartsWith("fixture.", StringComparison.Ordinal))
        {
            if (fixture is null || !fixture.Evidence.Result.Rows[fixture.RowIndex].TryGetValue(source[8..], out cell)) throw new InvalidOperationException($"Missing fixture value '{source}'. Select a fixture, enter a manual value, or disable the field.");
        }
        else if (source.StartsWith("response.", StringComparison.Ordinal))
        {
            if (responseBody is null) throw new InvalidOperationException("A complete API response is required for this parameter.");
            using var document = JsonDocument.Parse(responseBody);
            cell = document.RootElement;
            var pointer = source[9..];
            if (pointer.Length > 0 && !pointer.StartsWith('/')) throw new InvalidDataException("Response bindings use JSON Pointer.");
            foreach (var part in pointer.Split('/').Skip(1))
            {
                var key = part.Replace("~1", "/").Replace("~0", "~");
                if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty(key, out var next)) cell = next;
                else if (cell.ValueKind == JsonValueKind.Array && int.TryParse(key, out var index) && index >= 0 && index < cell.GetArrayLength()) cell = cell[index];
                else throw new InvalidOperationException($"Missing response value '{pointer}'.");
            }
            cell = cell.Clone();
        }
        else throw new InvalidDataException($"Unknown binding '{source}'.");
        if (Text(cell).Contains(Redaction.Mask, StringComparison.Ordinal)) throw new InvalidOperationException("A redacted value cannot be used as a binding.");
        return cell;
    }

    public static RequestDefinition Request(RequestDefinition template, FixtureSelection? fixture, TestRun? run = null, IReadOnlyDictionary<string, string>? selectedAttempts = null)
    {
        var request = PackageJson.Clone(template);
        string Replace(string text, bool url = false) => Marker.Replace(text, m =>
        {
            var value = Text(Resolve(m.Value, fixture, run: run, sources: request.CaptureSources, selectedAttempts: selectedAttempts));
            return url ? Uri.EscapeDataString(value) : value;
        });
        request.Url = Replace(request.Url, true);
        if (request.Url.Contains("{{", StringComparison.Ordinal)) throw new InvalidDataException("An incomplete URL binding could not be resolved.");
        foreach (var f in request.Query.Concat(request.Headers).Where(f => f.Enabled)) f.Value = Replace(f.Value);
        if (request.SendBody && request.BodyKind is RequestBodyKind.FormUrlEncoded or RequestBodyKind.Multipart)
            foreach (var part in request.Parts.Where(p => p.Enabled && !p.Binary)) part.Value = Replace(part.Value);
        if (request.Query.Concat(request.Headers).Any(f => f.Enabled && f.Value.Contains("{{", StringComparison.Ordinal))) throw new InvalidDataException("An incomplete field binding could not be resolved.");
        if (request.SendBody && request.BodyKind == RequestBodyKind.Text && request.Body.Contains("{{", StringComparison.Ordinal))
        {
            if (!request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Body fixture bindings currently require JSON; use a complete binding as a JSON string value.");
            var node = JsonNode.Parse(request.Body);
            request.Body = Visit(node)?.ToJsonString(PackageJson.Options) ?? "null";
        }
        return request;

        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonValue val && val.TryGetValue<string>(out var text) && text.Contains("{{", StringComparison.Ordinal)) return JsonNode.Parse(Resolve(text, fixture, run: run, sources: request.CaptureSources, selectedAttempts: selectedAttempts).GetRawText());
            if (node is JsonObject obj) foreach (var p in obj.ToArray()) obj[p.Key] = Visit(p.Value?.DeepClone());
            if (node is JsonArray arr) for (var i = 0; i < arr.Count; i++) arr[i] = Visit(arr[i]?.DeepClone());
            return node;
        }
    }
    public static bool RequiresFixture(RequestDefinition request)
    {
        var values = request.Query.Concat(request.Headers).Where(f => f.Enabled).Select(f => f.Value).Append(request.Url);
        if (request.SendBody && request.BodyKind == RequestBodyKind.Text) values = values.Append(request.Body);
        if (request.SendBody && request.BodyKind is RequestBodyKind.Multipart or RequestBodyKind.FormUrlEncoded)
            values = values.Concat(request.Parts.Where(p => p.Enabled && !p.Binary).Select(p => p.Value));
        return values.Any(v => v.Contains("{{fixture.", StringComparison.Ordinal));
    }
    public static IEnumerable<string> Dependencies(RequestDefinition request)
    {
        var values = request.Query.Concat(request.Headers).Where(f => f.Enabled).Select(f => f.Value).Append(request.Url);
        if (request.SendBody && request.BodyKind == RequestBodyKind.Text) values = values.Append(request.Body);
        if (request.SendBody && request.BodyKind is RequestBodyKind.Multipart or RequestBodyKind.FormUrlEncoded) values = values.Concat(request.Parts.Where(p => p.Enabled && !p.Binary).Select(p => p.Value));
        return Regex.Matches(string.Join("\n", values), @"\{\{case\.([^.{}]+)\.").Select(m => m.Groups[1].Value).Distinct();
    }
}
