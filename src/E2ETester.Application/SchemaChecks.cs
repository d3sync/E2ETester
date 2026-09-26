using System.Text.Json;
using Json.Schema;

namespace E2ETester.Application;

public static class SchemaChecks
{
    public static JsonSchema Build(string text)
    {
        if (text.Length > 100_000) throw new InvalidDataException("Assertion schema exceeds 100 KB.");
        using var document = JsonDocument.Parse(text);
        void Inspect(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name is "$ref" or "$dynamicRef" or "$recursiveRef" && !property.Value.GetString()!.StartsWith('#'))
                        throw new InvalidDataException("Assertion schemas must bundle references locally; network retrieval is disabled.");
                    Inspect(property.Value);
                }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Inspect(item);
        }
        Inspect(document.RootElement);
        return JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry { Fetch = (_, _) => throw new InvalidDataException("External schema retrieval is disabled.") } });
    }
    public static (bool Valid, string Details) Evaluate(string schema, string body)
    {
        using var document=JsonDocument.Parse(body);
        var result=Build(schema).Evaluate(document.RootElement,new EvaluationOptions { OutputFormat=OutputFormat.List });
        var errors=new List<string>();
        void Visit(EvaluationResults node)
        {
            if (!node.IsValid && node.Errors is not null) foreach(var error in node.Errors) errors.Add(node.InstanceLocation+" "+error.Key+": "+error.Value);
            if(node.Details is not null) foreach(var child in node.Details) Visit(child);
        }
        Visit(result);
        return (result.IsValid,result.IsValid?"JSON Schema matched":string.Join("\n",errors.Take(50)));
    }
}
