using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace E2ETester.Application;

public static class SpecificationDocument
{
    public static string Normalize(string text)
    {
        if (text.Length > 5_000_000) throw new InvalidDataException("Specification exceeds 5 MB.");
        if (text.TrimStart().StartsWith('{')) { using var json = JsonDocument.Parse(text); return json.RootElement.GetRawText(); }
        var parser=new Parser(new StringReader(text));int nesting=0,events=0;
        while(parser.MoveNext())
        {
            if(++events>200_000)throw new InvalidDataException("YAML document has too many nodes.");
            if(parser.Current is YamlDotNet.Core.Events.MappingStart or YamlDotNet.Core.Events.SequenceStart) nesting++;
            if(parser.Current is YamlDotNet.Core.Events.MappingEnd or YamlDotNet.Core.Events.SequenceEnd) nesting--;
            if(nesting>64)throw new InvalidDataException("YAML nesting exceeds 64 levels.");
        }
        var stream = new YamlStream(); stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1) throw new InvalidDataException("Use one YAML document.");
        var count = 0;
        JsonNode? Convert(YamlNode node, int depth)
        {
            if (++count > 100_000 || depth > 64) throw new InvalidDataException("YAML nesting or expansion limit exceeded.");
            if (node is YamlMappingNode map)
            {
                var result = new JsonObject();
                foreach (var pair in map.Children)
                {
                    if (pair.Key is not YamlScalarNode key || key.Value is null) throw new InvalidDataException("YAML keys must be strings.");
                    result.Add(key.Value, Convert(pair.Value, depth + 1));
                }
                return result;
            }
            if (node is YamlSequenceNode array) return new JsonArray(array.Children.Select(n => Convert(n, depth + 1)).ToArray());
            var scalar = (YamlScalarNode)node; var value = scalar.Value ?? "";
            if (scalar.Style == ScalarStyle.Plain && scalar.Tag.ToString() != "tag:yaml.org,2002:str")
            {
                if (value is "null" or "Null" or "NULL" or "~" or "") return null;
                if (bool.TryParse(value, out var boolean)) return JsonValue.Create(boolean);
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) return JsonValue.Create(number);
            }
            return JsonValue.Create(value);
        }
        return Convert(stream.Documents[0].RootNode, 0)!.ToJsonString();
    }

    // Caller supplies only explicitly selected documents. Import never fetches references.
    public static string Bundle(string text, IReadOnlyDictionary<string, string>? documents = null)
    {
        var root = JsonNode.Parse(Normalize(text))!.AsObject();
        var refs = new JsonObject(); var imported = new Dictionary<string, string>();
        var expanded = 0;
        void Walk(JsonNode? node, string source, int depth)
        {
            if (++expanded > 100_000 || depth > 64) throw new InvalidDataException("Reference expansion limit exceeded.");
            if (node is JsonObject obj)
            {
                if (obj["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var value))
                {
                    var split = value.Split('#', 2); var document = split[0];
                    if(split.Length==2&&split[1].Length>0&&!split[1].StartsWith('/'))throw new InvalidDataException("OpenAPI references must use JSON Pointer fragments.");
                    if (document.Length > 0)
                    {
                        var key = source.Length == 0 ? document : new Uri(new Uri("https://bundle.invalid/" + source), document).AbsolutePath.TrimStart('/');
                        if (Uri.TryCreate(document, UriKind.Absolute, out _) || key.Contains("..") || documents is null || !documents.TryGetValue(key, out var content))
                            throw new InvalidDataException($"Reference '{key}' needs an explicitly supplied local document. Remote references are not fetched.");
                        if (!imported.TryGetValue(key, out var id))
                        {
                            if (imported.Count >= 32) throw new InvalidDataException("At most 32 reference documents are supported.");
                            id = "doc" + imported.Count; imported[key] = id;
                            var child = JsonNode.Parse(Normalize(content)); refs[id] = child;
                            Walk(child, key, depth + 1);
                        }
                        obj["$ref"] = "#/x-e2e-imports/" + id + (split.Length == 2 ? split[1] : "");
                    }
                    else if (source.Length > 0) obj["$ref"] = "#/x-e2e-imports/" + imported[source] + (split.Length == 2 ? split[1] : "");
                }
                foreach (var child in obj.ToArray()) Walk(child.Value, source, depth + 1);
            }
            else if (node is JsonArray array) foreach (var child in array) Walk(child, source, depth + 1);
        }
        Walk(root, "", 0);
        if (refs.Count > 0) root.Add("x-e2e-imports", refs);
        var result = root.ToJsonString();
        if (result.Length > 5_000_000) throw new InvalidDataException("Bundled specification exceeds 5 MB.");
        return result;
    }
}
