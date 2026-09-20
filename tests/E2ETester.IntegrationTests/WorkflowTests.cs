using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
internal static class WorkflowTests
{
    public static async Task RunAsync(Action<bool,string> check, Func<Func<Task>,string,Task> reject, string directory)
    {
        using var store=new SqliteRunStore(Path.Combine(directory,"workflow"));var transport=new CountingTransport();var coordinator=new RunCoordinator(store,transport);
        var package=SuiteDefaults.Create();package.Cases.Add(new TestCase { Id="business",Title="Business",Request=new(){Url="/business",IdentityRef="cloud-user"},Assertions=[new()] });
        check(package.Cases[0].Kind==TestStepKind.SwaggerAuthentication && package.Cases[1].Request.Url=="/whoami", "suite begins with auth-only and remote IP");
        check(SuiteDefaults.Ensure(package).Cases.Count==3,"suite setup is not duplicated");
        var run=await coordinator.StartAsync(package,package.BaseUrl);var identity=new IdentitySession("cloud-user",AuthKind.Bearer,"workflow-secret");
        Task<Attempt> Send(int index,IdentitySession session)=>coordinator.ExecuteAsync(run,package.Cases[index],package.Cases[index].Request,package.Cases[index].Assertions,session,default);
        await reject(()=>Send(2,identity),"business case cannot bypass sign-in");
        await reject(()=>Send(0,new("cloud-user",AuthKind.None)),"auth-only step rejects missing bearer token");
        var auth=await Send(0,identity);
        check(auth.Verdict==Verdict.Pass && auth.Response is null && transport.Count==0,"auth-only records token availability without HTTP");
        await reject(()=>Send(2,identity),"business case cannot bypass remote-IP validation");
        await Send(1,identity);await Send(2,identity);
        check(transport.Count==2 && run.Attempts.Count==3,"validated suite records each HTTP attempt");
        await Send(0,identity);
        await reject(()=>Send(2,identity),"new sign-in requires fresh remote-IP validation");
        var report=Reports.Json(run);check(!report.Contains("workflow-secret") && Reports.Text(run).Contains("No API request sent"),"auth report is readable and excludes token");
        await Reports.WriteAtomicAsync(Path.Combine(directory,"workflow-report.json"),report);
        var restored=await coordinator.ResumeAsync();check(restored!.Attempts[0].Verdict==Verdict.Pass,"auth-only evidence survives recovery");
        var renamed = await coordinator.ExecuteAsync(run, package.Cases[0], package.Cases[0].Request with { IdentityRef="renamed-user" }, package.Cases[0].Assertions, new("renamed-user",AuthKind.Bearer,"replacement-secret"), default);
        check(renamed.Verdict==Verdict.Pass && renamed.IdentityAlias=="renamed-user" && run.Attempts[0].IdentityAlias=="cloud-user", "renamed authentication uses draft identity and preserves history");
        const string spec="""
        {"openapi":"3.0.3","servers":[{"url":"https://example.test/api"}],"components":{"parameters":{"id":{"name":"id","in":"path","required":true,"schema":{"type":"integer","default":12}}}},"paths":{"/items/{id}":{"parameters":[{"$ref":"#/components/parameters/id"}],"get":{"summary":"Read item","parameters":[{"name":"q","in":"query","example":"hello","required":false}],"responses":{"200":{}}},"post":{"requestBody":{"content":{"application/json":{"example":{"ok":true}}}},"responses":{"201":{}}}}}}
        """;
        var ops=OpenApiImport.Parse(spec,"https://fallback.test");
        check(ops.Count==2 && ops[0].Case.Request.Url=="/items/12" && ops[0].Case.Request.Query[0].Value=="hello","OpenAPI local refs, path values and query examples import");
        check(ops[1].Case.Request.SendBody && ops[1].Case.Request.Body.Contains("true") && ops[1].Case.Assertions[0].Expected=="201","OpenAPI JSON request and success response import");
        await reject(()=>Task.Run(()=>OpenApiImport.Parse(spec.Replace("#/components/parameters/id","https://outside.test/ref"),"https://fallback.test")),"OpenAPI does not fetch remote references");
        var swagger=OpenApiImport.Parse("""{"swagger":"2.0","host":"example.test","basePath":"/api","schemes":["https"],"paths":{"/ping":{"get":{"responses":{"204":{}}}}}}""","https://fallback.test");
        check(swagger[0].BaseUrl=="https://example.test/api" && swagger[0].Case.Assertions[0].Expected=="204","Swagger 2.0 imports");
        await reject(()=>Task.Run(()=>OpenApiImport.Parse("<html>Swagger UI</html>","https://fallback.test")),"Swagger HTML is not silently treated as a spec");
    }
    private sealed class CountingTransport : IHttpTransport
    {
        public int Count { get; private set; }
        public Task<ResponseSnapshot> SendAsync(RequestDefinition request,Uri uri,IdentitySession identity,CancellationToken cancellationToken) {Count++;return Task.FromResult(new ResponseSnapshot {StatusCode=200,Body="127.0.0.1"});}
    }
}
