using System.Text.Json;
using System.Text.RegularExpressions;
using E2ETester.Core;

namespace E2ETester.Application;

public sealed record CollectionImport(TestPackage Package, List<string> Warnings);
public static class PostmanImport
{
    public static CollectionImport Parse(string text, string fallbackBaseUrl)
    {
        if (text.Length > 5_000_000) throw new InvalidDataException("Collection exceeds 5 MB.");
        using var document = JsonDocument.Parse(text); var root = document.RootElement;
        var info = root.GetProperty("info");
        if (!String(info, "schema").Contains("/v2.1.0/")) throw new InvalidDataException("Export a Postman Collection v2.1 JSON file.");
        var warnings = new List<string>(); var cases = new List<TestCase>();
        var variables = new Dictionary<string, string>();
        string Expand(string value) => Regex.Replace(value, @"\{\{([^{}]+)\}\}", m =>
        {
            if (variables.TryGetValue(m.Groups[1].Value, out var replacement) && !replacement.Contains("{{")) return replacement;
            warnings.Add("Provide a value for variable " + m.Groups[1].Value + "."); return "REPLACE_" + m.Groups[1].Value;
        });
        void Visit(JsonElement group, string prefix, JsonElement auth, int depth)
        {
            if (depth > 32) throw new InvalidDataException("Collection nesting exceeds 32 folders.");
            var previous = new Dictionary<string, string>(variables);
            if (group.TryGetProperty("variable", out var vars)) foreach (var v in vars.EnumerateArray()) variables[String(v,"key")] = String(v,"value");
            if (group.TryGetProperty("auth", out var ownAuth)) auth = ownAuth;
            if (group.TryGetProperty("event", out var events) && events.GetArrayLength() > 0) warnings.Add(prefix + ": scripts are not executed or translated. Recreate their setup and assertions.");
            if (group.TryGetProperty("item", out var children))
                foreach (var child in children.EnumerateArray()) Visit(child, prefix.Length == 0 ? String(child,"name") : prefix + " / " + String(child,"name"), auth, depth + 1);
            if (group.TryGetProperty("request", out var raw))
            {
                if (cases.Count >= 1000) throw new InvalidDataException("Collection exceeds 1,000 requests.");
                var request = new RequestDefinition();
                if (raw.ValueKind == JsonValueKind.String) request.Url = Expand(raw.GetString()!);
                else
                {
                    request.Method = String(raw,"method"); var url = raw.GetProperty("url");
                    request.Url = Expand(url.ValueKind == JsonValueKind.String ? url.GetString()! : String(url,"raw"));
                    if (raw.TryGetProperty("auth", out var requestAuth)) auth = requestAuth;
                    if (raw.TryGetProperty("header", out var headers)) foreach (var h in headers.EnumerateArray()) request.Headers.Add(new() { Name=String(h,"key"), Value=Expand(String(h,"value")), Enabled=!Disabled(h) });
                    if (url.ValueKind == JsonValueKind.Object)
                    {
                        if (url.TryGetProperty("variable", out var pathVariables)) foreach (var v in pathVariables.EnumerateArray()) request.Url=request.Url.Replace(":"+String(v,"key"),Uri.EscapeDataString(Expand(String(v,"value"))));
                        if (url.TryGetProperty("query", out var query))
                        {
                            request.Url=request.Url.Split('?')[0];
                            foreach (var q in query.EnumerateArray()) request.Query.Add(new() { Name=Expand(String(q,"key")), Value=Expand(String(q,"value")), Enabled=!Disabled(q) });
                        }
                    }
                    if (raw.TryGetProperty("body", out var body) && !Disabled(body))
                    {
                        var mode=String(body,"mode"); request.SendBody=true;
                        if (mode=="raw")
                        {
                            request.Body=Expand(String(body,"raw"));
                            var jsonBody=body.TryGetProperty("options",out var opts)&&opts.TryGetProperty("raw",out var rawOptions)&&String(rawOptions,"language")=="json";
                            request.ContentType=request.Headers.FirstOrDefault(h=>h.Enabled&&h.Name.Equals("Content-Type",StringComparison.OrdinalIgnoreCase))?.Value ?? (jsonBody?"application/json":"text/plain");
                        }
                        else if (mode=="urlencoded") { request.BodyKind=RequestBodyKind.FormUrlEncoded; foreach(var f in body.GetProperty(mode).EnumerateArray()) request.Parts.Add(new() { Name=String(f,"key"), Value=Expand(String(f,"value")), Enabled=!Disabled(f) }); }
                        else if (mode=="formdata")
                        {
                            request.BodyKind=RequestBodyKind.Multipart;
                            foreach(var f in body.GetProperty(mode).EnumerateArray())
                            {
                                if(String(f,"type")=="file") { warnings.Add(prefix+": attach multipart file '"+String(f,"key")+"' manually."); continue; }
                                request.Parts.Add(new() { Name=String(f,"key"), Value=Expand(String(f,"value")), Enabled=!Disabled(f) });
                            }
                        }
                        else { request.SendBody=false; warnings.Add(prefix+": unsupported body mode '"+mode+"'; configure the body before sending."); }
                    }
                }
                if (auth.ValueKind==JsonValueKind.Object && String(auth,"type") is { } type && type!="noauth")
                {
                    if (type=="bearer")
                    {
                        var token=auth.GetProperty("bearer").EnumerateArray().FirstOrDefault(v=>String(v,"key")=="token");
                        request.Headers.Add(new() { Name="Authorization", Value="Bearer "+Expand(String(token,"value")) });
                    }
                    else if(type=="apikey")
                    {
                        var values=auth.GetProperty(type).EnumerateArray().ToDictionary(v=>String(v,"key"),v=>Expand(String(v,"value")));
                        (values.GetValueOrDefault("in")=="query"?request.Query:request.Headers).Add(new() { Name=values.GetValueOrDefault("key","REPLACE_API_KEY_HEADER"),Value=values.GetValueOrDefault("value","") });
                    }
                    else if(type=="basic")
                    {
                        var values=auth.GetProperty(type).EnumerateArray().ToDictionary(v=>String(v,"key"),v=>Expand(String(v,"value")));
                        request.Headers.Add(new() { Name="Authorization",Value="Basic "+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(values.GetValueOrDefault("username","")+":"+values.GetValueOrDefault("password",""))) });
                    }
                    else warnings.Add(prefix+": configure inherited/request authentication '"+type+"' manually.");
                }
                cases.Add(new() { Id="postman-"+(cases.Count+1), Title=prefix, Request=request, Instructions="Imported from Postman. Review import warnings, authentication and values. Add assertions: Postman scripts are not translated.", Assertions=[] });
            }
            variables=previous;
        }
        Visit(root,"",default,0);
        var package=new TestPackage { Title=String(info,"name"), BaseUrl=fallbackBaseUrl, Cases=cases, Description="Imported Postman collection. No scripts executed. No automatic PASS assertions added." };
        PackageJson.Validate(package);
        return new(package,warnings.Distinct().ToList());
    }
    private static bool Disabled(JsonElement value)=>value.TryGetProperty("disabled",out var d)&&d.ValueKind==JsonValueKind.True;
    private static string String(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var p)?p.ValueKind==JsonValueKind.String?p.GetString()!:p.ToString():"";
}
