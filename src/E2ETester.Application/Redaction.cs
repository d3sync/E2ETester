using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using E2ETester.Core;

namespace E2ETester.Application;

public sealed class Redaction
{
    public const string Mask = "[REDACTED]";
    private readonly string[] secrets;
    private static readonly string[] SensitiveNames = ["authorization", "proxy-authorization", "cookie", "set-cookie", "password", "passwd", "secret", "token", "api-key", "apikey", "api_key", "access_token", "refresh_token", "id_token", "client_secret"];

    public Redaction(IEnumerable<string> values) => secrets = values.Where(s => !string.IsNullOrEmpty(s) && s != Mask)
        .SelectMany(s => new[] { s, Uri.EscapeDataString(s), System.Net.WebUtility.UrlEncode(s), System.Text.Json.JsonSerializer.Serialize(s)[1..^1] })
        .Distinct().OrderByDescending(s => s.Length).ToArray();

    public static bool IsSensitive(string name) => SensitiveNames.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
    public string Text(string value)
    {
        foreach (var secret in secrets) value = value.Replace(secret, Mask, StringComparison.Ordinal);
        return value;
    }
    public string Body(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            Scrub(node);
            return Text(node?.ToJsonString(PackageJson.Options) ?? body);
        }
        catch (System.Text.Json.JsonException) { return Text(body); }
    }
    private void Scrub(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
                if (IsSensitive(property.Key)) obj[property.Key] = Mask; else Scrub(property.Value);
        if (node is JsonArray array) foreach (var item in array) Scrub(item);
    }

    public string Url(string url) => Text(Regex.Replace(Regex.Replace(url, @"(?i)(https?://)[^/@\s]+@", "$1" + Mask + "@"), @"([?&])([^=&]+)=([^&#]*)", m =>
        IsSensitive(Uri.UnescapeDataString(m.Groups[2].Value)) ? $"{m.Groups[1]}{m.Groups[2]}={Mask}" : m.Value));

    public RequestDefinition Request(RequestDefinition request)
    {
        var copy = PackageJson.Clone(request);
        copy.Url = Url(copy.Url);
        copy.ProxyUrl = Url(copy.ProxyUrl);
        copy.Body = request.BodySensitive ? Mask : Body(copy.Body);
        if (copy.BodyKind == RequestBodyKind.Binary && copy.Body != Mask) copy.Body = Binary(copy.Body);
        foreach (var part in copy.Parts) part.Value = request.BodySensitive || part.Sensitive || IsSensitive(part.Name) ? Mask : part.Binary ? Binary(part.Value) : Body(part.Value);
        foreach (var field in copy.Query.Concat(copy.Headers)) field.Value = field.Sensitive || IsSensitive(field.Name) ? Mask : Text(field.Value);
        return copy;
    }

    public TestPackage Package(TestPackage package)
    {
        var copy = PackageJson.Clone(package);
        copy.BaseUrl = Url(copy.BaseUrl);
        foreach (var c in copy.Cases.Concat(copy.GuidedActions ?? []))
        {
            c.Request = Request(c.Request);
            if (c.Preparation is { } preparation)
            {
                preparation.CaptureRequest = Request(preparation.CaptureRequest);
                preparation.UpdateRequest = Request(preparation.UpdateRequest);
                preparation.KeyJson = Text(preparation.KeyJson); preparation.ValueJson = Text(preparation.ValueJson);
            }
            c.Instructions = Text(c.Instructions);
            c.Assertions = c.Assertions.Select(a => a with { Expected = IsSensitive(a.Selector) ? (a.Kind == CheckKind.JsonEquals ? "\"" + Mask + "\"" : Mask) : Body(a.Expected) }).ToList();
            foreach (var check in c.DatabaseChecks)
                if (IsSensitive(check.Column) || copy.Queries.FirstOrDefault(q => q.Id == check.QueryId)?.SensitiveColumns.Contains(check.Column, StringComparer.OrdinalIgnoreCase) == true) check.ExpectedJson = "\"" + Mask + "\"";
        }
        copy.Queries = copy.Queries.Select(q => DatabaseQueries.SafeEvidence(q, new QueryResult()).Query).ToList();
        return PackageJson.Read<TestPackage>(Text(PackageJson.Write(copy)));
    }

    public ResponseSnapshot Response(ResponseSnapshot r) => r with
    {
        SessionSecrets = [],
        Requests = r.Requests.Select(RequestEvidence).ToList(),
        Redirects = r.Redirects.Select(h => h with { Url = Url(h.Url), Location = Url(h.Location) }).ToList(),
        Body = r.BodyEncoding == "base64" ? Binary(r.Body) : Body(r.Body), Error = r.Error is null ? null : Text(r.Error),
        Headers = r.Headers.ToDictionary(p => p.Key, p => IsSensitive(p.Key) ? new[] { Mask } : p.Value.Select(Text).ToArray())
    };
    public SentRequest RequestEvidence(SentRequest request) => request with
    {
        Url = Url(request.Url), Body = request.BodyEncoding == "base64" ? Binary(request.Body) : Body(request.Body),
        Headers = request.Headers.ToDictionary(h => h.Key, h => IsSensitive(h.Key) ? new[] { Mask } : h.Value.Select(Text).ToArray())
    };

    public static IEnumerable<string> Secrets(RequestDefinition request) => request.Headers.Concat(request.Query)
        .Where(f => f.Sensitive || IsSensitive(f.Name)).Select(f => f.Value)
        .Concat(request.BodySensitive ? new[] { request.Body } : BodySecrets(request.Body))
        .Concat(request.Parts.Where(p => request.BodySensitive || p.Sensitive || IsSensitive(p.Name)).SelectMany(p => p.Binary ? new[] { p.Value, Decode(p.Value) } : new[] { p.Value }))
        .Concat(request.BodySensitive && request.BodyKind == RequestBodyKind.Binary ? new[] { Decode(request.Body) } : [])
        .Concat(Regex.Matches(request.Url, @"[?&]([^=&]+)=([^&#]*)").Cast<Match>()
            .Where(m => IsSensitive(Uri.UnescapeDataString(m.Groups[1].Value)))
            .Select(m => Uri.UnescapeDataString(m.Groups[2].Value)));

    private static string Decode(string value) { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)); } catch (FormatException) { return ""; } }
    private string Binary(string value)
    {
        if (value == Mask) return Mask;
        var decoded = Decode(value);
        // Opaque binary is retained only when no known or named credential is detected.
        return Text(decoded) != decoded || BodySecrets(decoded).Any() ? Mask : value;
    }

    public static IEnumerable<string> BodySecrets(string body)
    {
        var values = new List<string>();
        try { Visit(JsonNode.Parse(body)); } catch (System.Text.Json.JsonException) { }
        return values;
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var p in obj)
                    if (IsSensitive(p.Key) && p.Value is not null) values.Add(p.Value.ToString()); else Visit(p.Value);
            if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
    }
}
