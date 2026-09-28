using System.Text.Json;
using System.Text.RegularExpressions;
using E2ETester.Core;

namespace E2ETester.Application;

public static class IdentityBindings
{
    private static readonly Regex Binding=new(@"^\{\{identity\.([^{}.]+)\.([^{}]+)\}\}$",RegexOptions.CultureInvariant);
    public static IEnumerable<string> References(TestCase test)
    {
        yield return test.Request.IdentityRef;
        foreach(var condition in test.AppliesWhen??[])yield return condition.IdentityRef;
        if(test.Preparation is {} plan)
        {
            if(KeyBinding(plan) is {} binding)yield return binding.Alias;
            if(plan.Kind==PreparationKind.ApiValue){yield return plan.CaptureRequest.IdentityRef;yield return plan.UpdateRequest.IdentityRef;}
        }
    }
    public static void Rename(TestCase test,string old,string replacement)
    {
        if(test.Request.IdentityRef==old)test.Request.IdentityRef=replacement;
        if(test.AppliesWhen is not null)test.AppliesWhen=test.AppliesWhen.Select(c=>c.IdentityRef==old?c with { IdentityRef=replacement }:c).ToList();
        if(test.Preparation is {} plan)
        {
            if(KeyBinding(plan) is {} binding && binding.Alias==old)plan.KeyJson=JsonSerializer.Serialize("{{identity."+replacement+"."+binding.Claim+"}}");
            if(plan.CaptureRequest.IdentityRef==old)plan.CaptureRequest.IdentityRef=replacement;
            if(plan.UpdateRequest.IdentityRef==old)plan.UpdateRequest.IdentityRef=replacement;
        }
    }
    public static (string Alias,string Claim)? KeyBinding(PreparationPlan plan)
    {
        var scalar=Preparations.Scalar(plan.KeyJson);
        if(scalar.ValueKind!=JsonValueKind.String)return null;
        var text=scalar.GetString()!;
        if(!text.Contains("{{"))return null;
        var match=Binding.Match(text);
        if(!match.Success)throw new InvalidDataException("Use a complete JSON string: {{identity.alias.claim}} for a preparation key.");
        if(plan.Kind!=PreparationKind.DatabaseValue)throw new InvalidDataException("Identity key bindings apply to database preparations.");
        return (match.Groups[1].Value,match.Groups[2].Value);
    }
    public static JsonElement Claim(IdentitySession identity,string name)
    {
        if(identity.Kind!=AuthKind.Bearer)throw new InvalidOperationException($"Identity '{identity.Alias}' needs a bearer JWT to read '{name}'.");
        try
        {
            var parts=identity.Secret.Split('.');
            if(parts.Length!=3)throw new FormatException();
            var part=parts[1].Replace('-','+').Replace('_','/');
            using var jwt=JsonDocument.Parse(Convert.FromBase64String(part.PadRight((part.Length+3)/4*4,'=')));
            if(!jwt.RootElement.TryGetProperty("exp",out var exp)||exp.ValueKind!=JsonValueKind.Number||!exp.TryGetInt64(out var seconds)||DateTimeOffset.FromUnixTimeSeconds(seconds)<=DateTimeOffset.UtcNow)
                throw new InvalidOperationException($"Identity '{identity.Alias}' needs an unexpired JWT with exp before resolving account claims.");
            if(!jwt.RootElement.TryGetProperty(name,out var value)||value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array)
                throw new InvalidOperationException($"Identity '{identity.Alias}' has no scalar '{name}' claim.");
            return value.Clone();
        }
        catch(Exception ex) when(ex is FormatException or JsonException or ArgumentOutOfRangeException)
        { throw new InvalidOperationException($"Identity '{identity.Alias}' does not contain a readable JWT. Sign in before resolving account claims."); }
    }
    public static async Task<PreparationPlan> ResolveAsync(PreparationPlan source,Func<string,Task<IdentitySession>> identity)
    {
        var plan=PackageJson.Clone(source);
        if(KeyBinding(source) is {} binding)plan.KeyJson=Claim(await identity(binding.Alias),binding.Claim).GetRawText();
        return plan;
    }
    public static async Task CheckSameAccountAsync(PreparationPlan plan,IdentitySession role,Func<string,Task<IdentitySession>> identity)
    {
        if(!PermissionIdentity.IsPermissionPlan(plan))return;
        if(KeyBinding(plan) is not {} binding)return;
        var owner=Claim(await identity(binding.Alias),binding.Claim);
        if(!JsonElement.DeepEquals(owner,Claim(role,binding.Claim)))throw new InvalidOperationException($"Identity '{role.Alias}' belongs to a different account than '{binding.Alias}' ({binding.Claim}). Sign in as the intended account for this role.");
    }
    public static async Task<List<string>> ApplicabilityAsync(TestCase test,Func<string,Task<IdentitySession>> identity)
    {
        var issues=new List<string>();
        foreach(var condition in test.AppliesWhen??[])
        {
            try
            {
                var actual=Claim(await identity(condition.IdentityRef),condition.Claim);var expected=Preparations.Scalar(condition.ExpectedJson);
                if(!JsonElement.DeepEquals(actual,expected))issues.Add($"{condition.Description} · {condition.IdentityRef}.{condition.Claim}: expected {condition.ExpectedJson}, actual {actual.GetRawText()}.");
            }
            catch(InvalidOperationException ex){issues.Add(ex.Message);}
        }
        return issues;
    }
}
