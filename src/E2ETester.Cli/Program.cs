using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("E2E Tester CLI\nrun --package suite.jsee --config host.json --output results [--base-url URL]\nimport --input collection.json|openapi.yaml --output suite.jsee --base-url URL [--references directory]\nExit: 0 PASS, 1 FAIL, 2 INCONCLUSIVE/configuration/execution error. No browser login. Tokens and database credentials use host-config environment variables.");
    return 0;
}
try
{
    var options=new Dictionary<string,string>();
    for(var i=1;i<args.Length;i+=2)
    {
        if(i+1>=args.Length || !args[i].StartsWith("--") || !options.TryAdd(args[i],args[i+1])) throw new InvalidDataException("Use unique --option value arguments.");
    }
    string Required(string key)=>options.TryGetValue(key,out var value)?value:throw new InvalidDataException("Missing "+key);
    foreach(var key in options.Keys) if(!new[]{"--package","--config","--output","--base-url","--input","--references"}.Contains(key)) throw new InvalidDataException("Unknown option "+key);
    if(args[0]=="import")
    {
        var input=Required("--input"); var text=await File.ReadAllTextAsync(input); var baseUrl=Required("--base-url");
        TestPackage package;
        using var json=JsonDocument.Parse(SpecificationDocument.Normalize(text));
        if(json.RootElement.TryGetProperty("info",out var info)&&info.TryGetProperty("schema",out _))
        {
            var result=PostmanImport.Parse(text,baseUrl);package=result.Package;
            foreach(var warning in result.Warnings) Console.Error.WriteLine("REVIEW: "+warning);
            package.Description += "\n"+string.Join("\n",result.Warnings);
        }
        else
        {
            var references=options.TryGetValue("--references",out var directory)?ImportFiles.References(directory):null;
            var operations=OpenApiImport.Parse(text,baseUrl,references);
            foreach(var operation in operations) operation.Case.Request.Url=RequestUrls.Resolve(operation.BaseUrl,operation.Case.Request).AbsoluteUri;
            package=new() { Title=Path.GetFileNameWithoutExtension(input),BaseUrl=baseUrl,Cases=operations.Select(o=>o.Case).ToList() };
        }
        PackageJson.Validate(package);await Reports.WriteAtomicAsync(Required("--output"),PackageJson.Write(package));
        Console.WriteLine($"Imported {package.Cases.Count} cases. Review before execution; no API request sent.");return 0;
    }
    if(args[0]!="run")throw new InvalidDataException("Use run or import.");
    var suite=PackageJson.Import(await File.ReadAllTextAsync(Required("--package")));
    var config=PackageJson.Read<AgentConfiguration>(await File.ReadAllTextAsync(Required("--config")));
    var output=Path.GetFullPath(Required("--output"));Directory.CreateDirectory(output);
    config=config with { Workspace=Path.Combine(output,"workspace"),AllowInteractiveLogin=false };
    using var cancel=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    var resultRun=await HeadlessRunner.RunAsync(suite,config,options.GetValueOrDefault("--base-url",suite.BaseUrl),output,cancel.Token);
    Console.WriteLine($"{Outcomes.ForRun(resultRun).ToString().ToUpperInvariant()}: {resultRun.Attempts.Count} attempts. Reports: {output}");
    return resultRun.FinishedAt is null ? 2 : Outcomes.ForRun(resultRun) switch { Verdict.Pass=>0,Verdict.Fail=>1,_=>2 };
}
catch(Exception ex) { Console.Error.WriteLine("Run could not complete: "+ex.Message);return 2; }
