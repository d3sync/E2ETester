using System.Text.Json;
using E2ETester.Core;
namespace E2ETester.Application;
public sealed record ApiOperation(string Label, string BaseUrl, TestCase Case);
public static class OpenApiImport
{
    public static List<ApiOperation> Parse(string json, string fallbackBaseUrl, IReadOnlyDictionary<string, string>? documents = null)
    {
        if (json.Length > 5_000_000) throw new InvalidDataException("OpenAPI document exceeds 5 MB.");
        using var doc = JsonDocument.Parse(SpecificationDocument.Bundle(json, documents)); var root = doc.RootElement;
        var version = Str(root, "openapi"); var swagger = Str(root, "swagger");
        if (!version.StartsWith("3.") && swagger != "2.0") throw new InvalidDataException("Use an OpenAPI 3.x or Swagger 2.0 JSON or YAML document, not the Swagger UI page.");
        var baseUrl = fallbackBaseUrl;
        if (root.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0) baseUrl = Str(servers[0], "url");
        else if (Str(root,"host") is { Length: > 0 } host) baseUrl = (root.TryGetProperty("schemes",out var schemes) && schemes.GetArrayLength()>0 ? schemes[0].GetString() : "https") + "://" + host + Str(root,"basePath");
        if (!Uri.TryCreate(baseUrl,UriKind.Absolute,out var address)) address = new Uri(new Uri(fallbackBaseUrl),baseUrl);
        if (address.Scheme is not ("http" or "https") || address.UserInfo.Length>0) throw new InvalidDataException("Review the OpenAPI server URL.");
        var result = new List<ApiOperation>();
        if (!root.TryGetProperty("paths", out var paths)) throw new InvalidDataException("The document has no paths.");
        JsonElement Resolve(JsonElement value)
        {
            var visited = new HashSet<string>();
            while (value.TryGetProperty("$ref",out var reference))
            {
                var path = reference.GetString()!;
                if (!path.StartsWith("#/") || !visited.Add(path)) throw new InvalidDataException("Only non-cyclic local OpenAPI references are supported.");
                value = root;
                foreach(var part in path[2..].Split('/')) value=value.GetProperty(part.Replace("~1","/").Replace("~0","~"));
            }
            return value;
        }
        foreach (var path in paths.EnumerateObject())
        {
            if (!path.Name.StartsWith('/')) continue;
            var item = Resolve(path.Value);
            foreach (var operation in item.EnumerateObject())
            {
                if (!new[]{"get","post","put","patch","delete","head","options","trace"}.Contains(operation.Name)) continue;
                var op = Resolve(operation.Value);
                var request = new RequestDefinition { Method=operation.Name.ToUpperInvariant(), Url=path.Name };
                var notes = new List<string> { "Imported from OpenAPI. Review path values, authentication, headers, body and expected results before sending. Nothing has run." };
                var parameters = new Dictionary<string,JsonElement>();
                foreach (var parent in new[]{item,op})
                    if (parent.TryGetProperty("parameters",out var ps)) foreach(var raw in ps.EnumerateArray()) { var p=Resolve(raw); parameters[Str(p,"in")+":"+Str(p,"name")]=p; }
                foreach(var p in parameters.Values)
                {
                    var name=Str(p,"name"); var location=Str(p,"in"); var required=p.TryGetProperty("required",out var req)&&req.ValueKind==JsonValueKind.True;
                    var schema=p.TryGetProperty("schema",out var sc)?Resolve(sc):p;
                    var value=Example(p,schema);
                    if(location=="path") { request.Url=request.Url.Replace("{"+name+"}",Uri.EscapeDataString(value.Length>0?value:"REPLACE_"+name)); notes.Add("Path parameter "+name+" must be reviewed."); }
                    else if(location is "query" or "header") (location=="query"?request.Query:request.Headers).Add(new Field { Name=name, Value=value, Required=required, Enabled=required || value.Length>0, Sensitive=false });
                    else if(location=="body") { request.SendBody=true; request.Body=JsonExample(p,schema); }
                    else notes.Add("Configure "+location+" parameter "+name+" manually.");
                }
                if(op.TryGetProperty("requestBody",out var body))
                {
                    body=Resolve(body);
                    if(body.TryGetProperty("content",out var content))
                    {
                        var media=content.EnumerateObject().OrderByDescending(m=>m.Name=="application/json").FirstOrDefault();
                        if(media.Name is not null) { request.ContentType=media.Name; request.SendBody=true; var schema=media.Value.TryGetProperty("schema",out var sc)?Resolve(sc):media.Value; request.Body=JsonExample(media.Value,schema); notes.Add("Body examples are a starting point; complex schemas and serialization styles need manual review."); }
                    }
                }
                var assertions=new List<Assertion>();
                if(op.TryGetProperty("responses",out var responses))
                {
                    var success=responses.EnumerateObject().Select(p=>p.Name).FirstOrDefault(n=>int.TryParse(n,out var code)&&code>=200&&code<300);
                    if(success is not null) assertions.Add(new Assertion { Expected=success });
                }
                var title=Str(op,"summary"); if(title.Length==0)title=Str(op,"operationId"); if(title.Length==0)title=path.Name;
                result.Add(new(request.Method+" "+path.Name+" · "+title,address.AbsoluteUri.TrimEnd('/'),new TestCase { Title=title, Instructions=string.Join("\n",notes), Request=request, Assertions=assertions }));
                if(result.Count>1000)throw new InvalidDataException("OpenAPI document exceeds 1,000 operations.");
            }
        }
        return result;
    }
    private static string Str(JsonElement node,string name)=>node.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()!:"";
    private static string Example(JsonElement node,JsonElement schema)
    {
        foreach(var parent in new[]{node,schema}) foreach(var key in new[]{"example","default"}) if(parent.TryGetProperty(key,out var value))return value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText();
        return "";
    }
    private static string JsonExample(JsonElement node,JsonElement schema)
    {
        foreach(var parent in new[]{node,schema}) foreach(var key in new[]{"example","default"}) if(parent.TryGetProperty(key,out var value))return value.GetRawText();
        return "{}";
    }
}
