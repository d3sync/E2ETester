using System.Xml.Linq;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

internal static class ImportAndCiTests
{
    public static async Task RunAsync(Action<bool,string> check,Func<Func<Task>,string,Task> reject,string directory,TestServer server)
    {
        const string yaml="""
        openapi: 3.1.0
        servers:
          - url: https://example.test/api
        paths:
          /items:
            get:
              parameters:
                - $ref: params.yaml#/parameter
              responses:
                '200': {description: OK}
        """;
        var operations=OpenApiImport.Parse(yaml,server.Url,new Dictionary<string,string> { ["params.yaml"]="parameter: {name: limit, in: query, required: true, schema: {type: integer, default: 4}}" });
        check(operations.Single().Case.Request.Query.Single().Value=="4","YAML and explicitly supplied external parameter references import");
        await reject(()=>Task.Run(()=>OpenApiImport.Parse(yaml,server.Url)),"Missing external references fail without fetching");
        await reject(()=>Task.Run(()=>SpecificationDocument.Normalize("a: &a [*a]")),"Cyclic YAML aliases are bounded");
        await reject(()=>Task.Run(()=>SpecificationDocument.Normalize("a: 1\na: 2")),"Duplicate YAML keys rejected");
        const string collection="""
        {"info":{"name":"Migration","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},"variable":[{"key":"host","value":"https://example.test"}],"event":[{"listen":"test","script":{"exec":["pm.test('x',()=>{});"]}}],"auth":{"type":"bearer","bearer":[{"key":"token","value":"{{token}}"}]},"item":[{"name":"Folder","item":[{"name":"Read","request":{"method":"GET","url":{"raw":"{{host}}/items?q=one&off=no","query":[{"key":"q","value":"one"},{"key":"q","value":"two"},{"key":"off","value":"no","disabled":true}]}}}]}]}
        """;
        var imported=PostmanImport.Parse(collection,server.Url);var request=imported.Package.Cases[0].Request;
        check(request.Url=="https://example.test/items"&&request.Query.Count==3&&!request.Query[2].Enabled,"Postman folder variables and duplicate/disabled query fields preserved");
        check(imported.Package.Cases[0].Title=="Folder / Read"&&imported.Package.Cases[0].Assertions.Count==0,"Postman order/titles retained without inventing assertions");
        check(imported.Warnings.Any(w=>w.Contains("scripts"))&&imported.Warnings.Any(w=>w.Contains("token")),"Unsupported scripts and missing authentication variables produce warnings");
        var assertion=new Assertion { Kind=CheckKind.JsonSchema,Expected="""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["id"],"properties":{"id":{"type":"integer","minimum":1}},"additionalProperties":false}""" };
        CheckResult Evaluate(string body)=>Assertions.Evaluate([assertion],new ResponseSnapshot { StatusCode=200,Body=body }).Single();
        check(Evaluate("{\"id\":1}").Verdict==Verdict.Pass&&Evaluate("{\"id\":\"1\"}").Verdict==Verdict.Fail,"JSON Schema validates types without coercion");
        check(Evaluate("{}").Verdict==Verdict.Fail&&Evaluate("{\"id\":0,\"extra\":true}").Verdict==Verdict.Fail,"JSON Schema checks required, numeric bounds and extra fields");
        check(Evaluate("not json").Verdict==Verdict.Fail,"JSON Schema rejects malformed response JSON");
        check(Assertions.Evaluate([assertion],new ResponseSnapshot { StatusCode=200,Body="{}",Truncated=true }).Single().Verdict==Verdict.Inconclusive,"Truncated responses cannot satisfy JSON Schema");
        await reject(()=>Task.Run(()=>SchemaChecks.Build("""{"$ref":"https://outside.test/schema"}""")),"Assertion schemas never fetch external resources");
        var suite=new TestPackage { SchemaVersion="1.7",BaseUrl=server.Url,Cases=[new() { Id="health",Title="Health",Request=new() { Url="/health" },Assertions=[new(),new() { Kind=CheckKind.JsonSchema,Expected="""{"type":"object","required":["status"],"properties":{"status":{"const":"ok"}}}""" }] }] };
        check(PackageJson.Import(PackageJson.Write(suite)).SchemaVersion=="1.7"&&SuiteDefaults.Ensure(suite).SchemaVersion=="1.7","Schema 1.7 survives import and suite defaults");
        await reject(()=>Task.Run(()=>PackageJson.Validate(suite with { SchemaVersion="1.6" })),"Older schema rejects the new assertion explicitly");
        var output=Path.Combine(directory,"ci-pass");
        var run=await HeadlessRunner.RunAsync(suite,new() { Workspace=Path.Combine(output,"workspace"),AllowHttp=true,AllowedBaseUrls=[server.Url] },server.Url,output,default);
        check(Outcomes.ForRun(run)==Verdict.Pass&&File.Exists(Path.Combine(output,"report.html")),"Headless shared engine passes and emits canonical HTML/JSON evidence");
        check(XDocument.Load(Path.Combine(output,"junit.xml")).Descendants("testcase").Count()==1,"CI produces parseable JUnit testcase output");
        suite.Cases[0].Request.IdentityRef="missing";output=Path.Combine(directory,"ci-missing");
        run=await HeadlessRunner.RunAsync(suite,new() { Workspace=Path.Combine(output,"workspace"),AllowHttp=true,AllowedBaseUrls=[server.Url] },server.Url,output,default);
        check(run.FinishedAt is null&&Outcomes.ForRun(run)==Verdict.Inconclusive&&XDocument.Load(Path.Combine(output,"junit.xml")).Descendants("error").Any(),"Missing identities are CI errors, never passing/skipped tests");
        suite.Cases[0].Request.IdentityRef="none";suite.Cases[0].Assertions=[new() { Expected="404" }];output=Path.Combine(directory,"ci-fail");
        run=await HeadlessRunner.RunAsync(suite,new() { Workspace=Path.Combine(output,"workspace"),AllowHttp=true,AllowedBaseUrls=[server.Url] },server.Url,output,default);
        check(Outcomes.ForRun(run)==Verdict.Fail&&XDocument.Load(Path.Combine(output,"junit.xml")).Descendants("failure").Any(),"Failed checks produce JUnit failure evidence");
        if(OperatingSystem.IsWindows())
        {
            var connectionString=new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"ci-prepare.db"),Pooling=false }.ToString();
            await using(var connection=new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
            {
                await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE state(id INTEGER PRIMARY KEY, value INTEGER); INSERT INTO state VALUES(1,15)";await command.ExecuteNonQueryAsync();
            }
            var plan=new PreparationPlan { Provider=DatabaseProvider.Sqlite,ConnectionRef="ci-db",Table="state",ValueColumn="value",ValueJson="4" };
            suite.Cases[0].Preparation=plan;
            const string variable="E2ETESTER_CI_DB_TEST";Environment.SetEnvironmentVariable(variable,connectionString);
            try
            {
                output=Path.Combine(directory,"ci-restoration");
                run=await HeadlessRunner.RunAsync(suite,new() { Workspace=Path.Combine(output,"workspace"),AllowHttp=true,AllowedBaseUrls=[server.Url],AllowPreparation=true,ApprovedPreparations=[plan],Databases=[new("ci-db",DatabaseProvider.Sqlite,variable,true)] },server.Url,output,default);
                var target=new DatabasePreparationTarget(new("ci-db",DatabaseProvider.Sqlite,connectionString));
                check(Outcomes.ForRun(run)==Verdict.Fail&&run.FinishedAt is not null&&await target.ReadAsync(plan,default)=="15","Headless runner restores prepared database values after a failed assertion");
                check(run.PreparationLog?.Any(p=>p.Status=="restored")==true,"Headless reports retain restoration audit");
            }
            finally { Environment.SetEnvironmentVariable(variable,null); }
        }
    }
}
