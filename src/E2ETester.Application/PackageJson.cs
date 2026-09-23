using System.Text.Json;
using System.Text.Json.Serialization;
using E2ETester.Core;

namespace E2ETester.Application;

public static class PackageJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("The document is empty.");
    public static T Clone<T>(T value) => Read<T>(Write(value));

    public static TestPackage Import(string json)
    {
        if (json.Length > 5_000_000) throw new InvalidDataException("Package exceeds the 5 MB limit.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Package must be a JSON object.");
        foreach (var name in new[] { "schemaVersion", "id", "title", "cases" })
            if (!document.RootElement.TryGetProperty(name, out _)) throw new InvalidDataException($"$.{name} is required.");
        RejectNulls(document.RootElement, "$");
        var package = Read<TestPackage>(json);
        Validate(package);
        return package with { SchemaVersion = package.Cases.Any(c => c.Preparation is not null) ? "1.5" : "1.4" };
    }

    private static void RejectNulls(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Null && p.Name != "parentId")
                    throw new InvalidDataException($"{path}.{p.Name}: null is not allowed.");
                RejectNulls(p.Value, $"{path}.{p.Name}");
            }
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new InvalidDataException($"{path}: null array item.");
                RejectNulls(item, path + "[]");
            }
    }

    public static void Validate(TestPackage package)
    {
        if (Write(package).Length > 5_000_000) throw new InvalidDataException("Package exceeds the 5 MB limit. Use smaller fixtures or fewer embedded files.");
        if (package.SchemaVersion is not ("1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5")) throw new InvalidDataException("Supported package schemas: 1.0, 1.1, 1.2, 1.3, 1.4 and 1.5.");
        if (string.IsNullOrWhiteSpace(package.Title) || string.IsNullOrWhiteSpace(package.Id)) throw new InvalidDataException("Package ID and title are required.");
        if (package.Cases.Count == 0 || package.Cases.Count > 1000) throw new InvalidDataException("A package must contain 1–1,000 cases.");
        if (package.Cases.Select(c => c.Id).Distinct().Count() != package.Cases.Count) throw new InvalidDataException("Case IDs must be unique.");
        if (package.Queries.Select(q => q.Id).Distinct().Count() != package.Queries.Count) throw new InvalidDataException("Query IDs must be unique.");
        foreach (var query in package.Queries) DatabaseQueries.Validate(query);
        foreach (var c in package.Cases)
        {
            if (c.Preparation is { } preparation) Preparations.Validate(preparation);
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Title)) throw new InvalidDataException("Each case needs an ID and title.");
            foreach (var dependency in ValueBindings.Dependencies(c.Request))
                if (!package.Cases.TakeWhile(p => p.Id != c.Id).Any(p => p.Id == dependency)) throw new InvalidDataException($"{c.Title}: source case '{dependency}' must exist earlier in the flow.");
            if (c.FixtureQueryId.Length > 0 && !package.Queries.Any(q => q.Id == c.FixtureQueryId)) throw new InvalidDataException($"{c.Title}: fixture query was not found.");
            if (c.DatabaseChecks.Select(d => d.Id).Distinct().Count() != c.DatabaseChecks.Count) throw new InvalidDataException($"{c.Title}: database check IDs must be unique.");
            foreach (var check in c.DatabaseChecks)
            {
                if (string.IsNullOrWhiteSpace(check.Id) || !package.Queries.Any(q => q.Id == check.QueryId)) throw new InvalidDataException($"{c.Title}: database check needs an ID and existing query.");
                using var expected = JsonDocument.Parse(check.ExpectedJson);
                if (check.Kind == DatabaseCheckKind.RowCount && (expected.RootElement.ValueKind != JsonValueKind.Number || !expected.RootElement.TryGetInt32(out var rows) || rows < 0)) throw new InvalidDataException("Row count must be a nonnegative integer.");
                if (check.Kind is DatabaseCheckKind.ScalarEquals or DatabaseCheckKind.ScalarNotEquals or DatabaseCheckKind.ScalarGreaterThan or DatabaseCheckKind.ScalarLessThan && string.IsNullOrWhiteSpace(check.Column)) throw new InvalidDataException("Scalar checks require a column name.");
                if (check.PollTimeoutSeconds is < 0 or > 600 || check.PollIntervalMilliseconds is < 100 or > 60000) throw new InvalidDataException("Polling must be 0–600 seconds with a 100–60,000 ms interval.");
                if (check.Kind is DatabaseCheckKind.ScalarGreaterThan or DatabaseCheckKind.ScalarLessThan && (expected.RootElement.ValueKind != JsonValueKind.Number || !expected.RootElement.TryGetDecimal(out _))) throw new InvalidDataException("Ordered scalar comparisons require a decimal number.");
            }
            if (c.ParentId is not null && !package.Cases.Any(p => p.Id == c.ParentId && p.Id != c.Id)) throw new InvalidDataException($"{c.Title}: variation parent does not exist.");
            ValidateRequest(c.Request, c.Title);
            foreach (var a in c.Assertions)
            {
                if (a.Kind == CheckKind.Status && (!int.TryParse(a.Expected, out var status) || status is < 100 or > 599))
                    throw new InvalidDataException($"{c.Title}: expected status must be 100–599.");
                if (a.Kind is CheckKind.JsonEquals or CheckKind.JsonExists or CheckKind.JsonGreaterThan or CheckKind.JsonLessThan or CheckKind.JsonArrayCount)
                {
                    if (a.Selector.Length > 0 && !a.Selector.StartsWith('/')) throw new InvalidDataException("JSON selectors use JSON Pointer, for example /user/id.");
                    if (a.Kind != CheckKind.JsonExists) { using var expected = JsonDocument.Parse(a.Expected); }
                }
                if (a.Kind is CheckKind.JsonGreaterThan or CheckKind.JsonLessThan && !decimal.TryParse(a.Expected, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _)) throw new InvalidDataException("Numeric comparisons require a decimal expectation.");
                if (a.Kind is CheckKind.JsonArrayCount or CheckKind.DurationLessThan && (!int.TryParse(a.Expected, out var count) || count < 0)) throw new InvalidDataException("Count and duration expectations must be nonnegative integers (milliseconds for duration).");
                if (a.Kind == CheckKind.StatusIn)
                {
                    using var statuses = JsonDocument.Parse(a.Expected);
                    if (statuses.RootElement.ValueKind != JsonValueKind.Array || statuses.RootElement.GetArrayLength() == 0 || statuses.RootElement.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out var status) || status is < 100 or > 599)) throw new InvalidDataException("Status membership requires a JSON array such as [200,201].");
                }
            }
        }
    }
    public static void ValidateRequest(RequestDefinition request, string title = "Request")
    {
            if (request.TimeoutSeconds is < 1 or > 600) throw new InvalidDataException($"{title}: timeout must be 1–600 seconds.");
            if (request.HttpVersion is not ("1.1" or "2.0" or "3.0") || request.MaxRedirects is < 0 or > 10) throw new InvalidDataException("Choose HTTP 1.1, 2.0 or 3.0 and a redirect limit of 0–10.");
            if (request.ProxyUrl.Length > 0 && (!Uri.TryCreate(request.ProxyUrl, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https") || proxy.UserInfo.Length > 0 || proxy.Query.Length > 0 || proxy.Fragment.Length > 0)) throw new InvalidDataException("Proxy URL must be HTTP(S) without credentials, query or fragment.");
            if (request.Parts.Count > 1000 || request.Parts.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Name.IndexOfAny(['\r', '\n']) >= 0 || p.FileName.IndexOfAny(['\r', '\n', '/', '\\']) >= 0)) throw new InvalidDataException("Body parts need simple names and filenames without paths or line breaks.");
            if (request.SendBody && request.BodyKind == RequestBodyKind.Binary && request.Body != Redaction.Mask) _ = Convert.FromBase64String(request.Body);
            if (request.SendBody && request.BodyKind == RequestBodyKind.Multipart)
                foreach (var part in request.Parts.Where(p => p.Enabled && p.Binary && p.Value != Redaction.Mask)) _ = Convert.FromBase64String(part.Value);
            _ = new HttpMethod(request.Method);
            if (string.IsNullOrWhiteSpace(request.Url)) throw new InvalidDataException($"{title}: URL is required.");
            foreach (var fields in new[] { request.Query, request.Headers })
            {
                if (fields.Select(f => f.Id).Distinct().Count() != fields.Count) throw new InvalidDataException($"{title}: duplicate field IDs.");
                if (fields.Any(f => string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Name))) throw new InvalidDataException($"{title}: fields require IDs and names.");
            }

        if (!Enum.IsDefined(request.BodyKind)) throw new InvalidDataException("Unknown request body format.");
        if (request.Url.Any(char.IsControl) || !Uri.TryCreate(new Uri("https://validation.example/"), request.Url, out var address) || address.Scheme is not ("http" or "https") || address.UserInfo.Length > 0)
            throw new InvalidDataException("Request URL must resolve to HTTP(S) without credentials.");
        foreach (var header in request.Headers)
            if (!System.Text.RegularExpressions.Regex.IsMatch(header.Name, @"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$") || header.Value.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidDataException("Invalid HTTP header name or value.");
    }

}
