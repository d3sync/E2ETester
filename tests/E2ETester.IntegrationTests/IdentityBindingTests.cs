using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class IdentityBindingTests
{
    public static async Task RunAsync(Action<bool,string> check,Func<Func<Task>,string,Task> reject,string directory,TestServer server)
    {
        string Jwt(string user,int permissions,int group=1,int hours=1)=>"e30."+Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { userid=user,permissions,groupid=group,accountKey=42,exp=DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeSeconds() }))).TrimEnd('=').Replace('+','-').Replace('/','_')+".test";
        var owner=new IdentitySession("cloud-user",AuthKind.Bearer,Jwt("user-b",15));var role=new IdentitySession("cloud-reader",AuthKind.Bearer,Jwt("user-b",4));
        Task<IdentitySession> Identity(string name)=>Task.FromResult(name==owner.Alias?owner:role);
        var template=new PreparationPlan { Provider=DatabaseProvider.Sqlite,ConnectionRef="admin",Table="accounts",KeyColumn="ID",KeyJson="\"{{identity.cloud-user.userid}}\"",ValueColumn="permissions",ValueJson="4",RequireFreshLogin=true,JwtClaim="permissions" };
        var resolved=await IdentityBindings.ResolveAsync(template,Identity);
        check(resolved.KeyJson=="\"user-b\""&&template.KeyJson.Contains("{{"),"Claim binding resolves the current tester without modifying the reusable template");
        var numeric=await IdentityBindings.ResolveAsync(template with { KeyJson="\"{{identity.cloud-user.accountKey}}\"" },Identity);
        check(numeric.KeyJson=="42","Numeric JWT claims retain their JSON type for database keys");
        await reject(()=>IdentityBindings.ResolveAsync(template with { KeyJson="\"{{identity.cloud-user.missing}}\"" },Identity),"Missing account claim fails before database access");
        await reject(()=>IdentityBindings.CheckSameAccountAsync(template,role with { Secret=Jwt("user-a",4) },Identity),"Matching permissions from another account cannot satisfy a bound role");
        await reject(()=>IdentityBindings.ResolveAsync(template,_=>Task.FromResult(owner with { Secret=Jwt("user-b",15,hours:-1) })),"Expired source tokens cannot resolve account targets");
        check(!PermissionIdentity.Matches(template,role)&&PermissionIdentity.Matches(resolved,role),"Unresolved key bindings cannot bypass account resolution");
        var connectionString=new SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"identity-binding.db"),Pooling=false }.ToString();
        await using(var connection=new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE accounts(ID TEXT PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES('user-a',15),('user-b',31)";await command.ExecuteNonQueryAsync();
        }
        var target=new DatabasePreparationTarget(new("admin",DatabaseProvider.Sqlite,connectionString));
        var recovery=new PreparationRecovery { Plan=resolved,OriginalJson=await target.ReadAsync(resolved,default) };
        await Preparations.ApplyAsync(recovery,target,()=>Task.CompletedTask,default);
        owner=owner with { Secret=Jwt("user-a",15) };
        await Preparations.RestoreAsync(recovery,target,()=>Task.CompletedTask,default);
        check(await target.ReadAsync(resolved,default)=="31"&&await target.ReadAsync(resolved with { KeyJson="\"user-a\"" },default)=="15","Restore uses the captured original and pinned target even when source identity changes");
        owner=owner with { Secret=Jwt("user-b",15) };
        var test=new TestCase { Id="role",Title="Role case",Request=new() { Url="/health",IdentityRef=role.Alias },Preparation=template,Assertions=[new()],AppliesWhen=[new(owner.Alias,"groupid","1","Expected fixture group")] };
        var package=new TestPackage { SchemaVersion="1.8",Cases=[test],BaseUrl=server.Url };
        check(PackageJson.Import(PackageJson.Write(package)).SchemaVersion=="1.8"&&SuiteDefaults.Ensure(package).SchemaVersion=="1.8","Schema 1.8 bindings and applicability survive round trip and defaults");
        await reject(()=>Task.Run(()=>PackageJson.Validate(package with { SchemaVersion="1.7" })),"Older schema cannot silently interpret bound targets");
        check((await IdentityBindings.ApplicabilityAsync(test,Identity)).Count==0,"Applicable tester claims pass without changing expected results");
        owner=owner with { Secret=Jwt("user-b",15,group:24) };
        var issues=await IdentityBindings.ApplicabilityAsync(test,Identity);
        check(issues.Count==1&&test.Assertions[0].Expected=="200","Different tester group is flagged without rewriting expected HTTP status");
        using(var store=new SqliteRunStore(Path.Combine(directory,"identity-override")))
        {
            var coordinator=new RunCoordinator(store,new HttpTransport());var run=await coordinator.StartAsync(package,server.Url);
            await reject(()=>coordinator.ExecuteAsync(run,test,test.Request,test.Assertions,role,default,preparationConfirmed:true),"Execution engine requires host evaluation of applicability");
            var attempt=await coordinator.ExecuteAsync(run,test,test.Request,test.Assertions,role,default,preparationConfirmed:true,bypassPrerequisites:true,applicabilityConfirmed:true,applicabilityIssues:issues);
            check(attempt.Verdict==Verdict.Pass&&attempt.Changes.Any(c=>c.Path=="execution.applicability"),"Explicit applicability override runs normal assertions and records the warning");
            await Reports.WriteAtomicAsync(Path.Combine(directory,"identity-report.json"),Reports.Json(run));
        }
        const string sourceVariable="E2E_TEST_BIND_SOURCE",roleVariable="E2E_TEST_BIND_ROLE";
        Environment.SetEnvironmentVariable(sourceVariable,Jwt("user-b",15));Environment.SetEnvironmentVariable(roleVariable,role.Secret);
        try
        {
            var output=Path.Combine(directory,"bound-ci");
            var config=new AgentConfiguration { Workspace=Path.Combine(output,"workspace"),AllowHttp=true,AllowedBaseUrls=[server.Url],Identities=[new(owner.Alias,AuthKind.Bearer,sourceVariable),new(role.Alias,AuthKind.Bearer,roleVariable)] };
            var run=await HeadlessRunner.RunAsync(package,config,server.Url,output,default);
            check(Outcomes.ForRun(run)==Verdict.Pass,"Headless/MCP resolves bound account and reuses matching role without database writes");
            Environment.SetEnvironmentVariable(sourceVariable,Jwt("user-b",15,group:24));output=Path.Combine(directory,"bound-ci-mismatch");
            run=await HeadlessRunner.RunAsync(package,config with { Workspace=Path.Combine(output,"workspace") },server.Url,output,default);
            check(Outcomes.ForRun(run)==Verdict.Inconclusive&&run.FinishedAt is null,"Headless execution reports applicability mismatch instead of silently overriding");
        }
        finally { Environment.SetEnvironmentVariable(sourceVariable,null);Environment.SetEnvironmentVariable(roleVariable,null); }
    }
}
