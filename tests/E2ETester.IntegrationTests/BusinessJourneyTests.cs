using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class BusinessJourneyTests
{
    public static async Task RunAsync(Action<bool,string> check,string directory)
    {
        var evidence=Path.Combine(directory,"business-journey");Directory.CreateDirectory(evidence);
        var connectionString=new SqliteConnectionStringBuilder { DataSource=Path.Combine(evidence,"journey.db"),Pooling=false }.ToString();
        async Task<object?> Sql(string sql)
        {
            await using var connection=new SqliteConnection(connectionString);await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText=sql;return await command.ExecuteScalarAsync();
        }
        await Sql("CREATE TABLE accounts(id INTEGER PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES(1,15); CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT UNIQUE);");
        var portListener=new TcpListener(IPAddress.Loopback,0);portListener.Start();var port=((IPEndPoint)portListener.LocalEndpoint).Port;portListener.Stop();
        var url=$"http://127.0.0.1:{port}";using var listener=new HttpListener();listener.Prefixes.Add(url+"/");listener.Start();
        var tokens=new Dictionary<string,long>();var gate=new object();
        var serving=Task.Run(async()=>
        {
            while(listener.IsListening)
            {
                HttpListenerContext context;try { context=await listener.GetContextAsync(); } catch(HttpListenerException){break;} catch(ObjectDisposedException){break;}
                try
                {
                    var response=context.Response;var request=context.Request;string body="{}";
                    if(request.Url!.AbsolutePath=="/login")
                    {
                        var permissions=Convert.ToInt64(await Sql("SELECT permissions FROM accounts WHERE id=1"));
                        var token="e30."+Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { permissions,exp=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }))).TrimEnd('=').Replace('+','-').Replace('/','_')+".synthetic";
                        lock(gate)tokens[token]=permissions;body=token;
                    }
                    else
                    {
                        long permissions;lock(gate) tokens.TryGetValue((request.Headers["Authorization"]??"").Replace("Bearer ",""),out permissions);
                        if(permissions==0)response.StatusCode=401;
                        else if(request.Url.AbsolutePath=="/whoami")body="127.0.0.1";
                        else if(request.HttpMethod=="POST")
                        {
                            if((permissions&8)==0)response.StatusCode=403;
                            else if(Convert.ToInt64(await Sql("SELECT count(*) FROM items"))>0){response.StatusCode=409;body="Duplicate item";}
                            else{await Sql("INSERT INTO items VALUES(1,'create-a')");response.StatusCode=201;body="{\"id\":1}";}
                        }
                        else if(request.HttpMethod=="DELETE") { await Sql("DELETE FROM items WHERE id=1");response.StatusCode=204;body=""; }
                        else body="{\"id\":1}";
                    }
                    var bytes=Encoding.UTF8.GetBytes(body);response.ContentLength64=bytes.Length;await response.OutputStream.WriteAsync(bytes);response.Close();
                }
                catch { context.Response.Abort();throw; }
            }
        });
        try
        {
            var timings=new List<object>();var timer=Stopwatch.StartNew();
            async Task<T> Measure<T>(string step,Func<Task<T>> action){var start=timer.ElapsedMilliseconds;var value=await action();timings.Add(new { step,durationMs=timer.ElapsedMilliseconds-start });return value;}
            using var client=new HttpClient();
            var admin=new IdentitySession("cloud-user",AuthKind.Bearer,await Measure("Sign in (synthetic issuer)",()=>client.GetStringAsync(url+"/login")));
            var package=SuiteDefaults.Create("Measured business journey",url);package.SchemaVersion="1.6";
            var create=new TestCase { Id="create",Title="Create item",Request=new() { Url="/items",Method="POST",IdentityRef="cloud-user" },Assertions=[new() { Expected="201" }] };
            var cleanup=new TestCase { Id="cleanup",Title="Delete created item",Request=new() { Url="/items/{{case.create./id}}",Method="DELETE",IdentityRef="cloud-user" },Assertions=[new() { Expected="204" }] };
            // Same API cleanup exposed as a guided action; it is not a required test case.
            package.GuidedActions=[cleanup];package.Cases.Add(create);
            var plan=new PreparationPlan { Provider=DatabaseProvider.Sqlite,ConnectionRef="db",Table="accounts",ValueColumn="permissions",ValueJson="4",RequireFreshLogin=true,JwtClaim="permissions" };
            var denied=new TestCase { Id="read-only",Title="Reader cannot create",Preparation=plan,Request=new() { Url="/items",Method="POST",IdentityRef="cloud-reader" },Assertions=[new() { Expected="403" }] };package.Cases.Add(denied);
            using var store=new SqliteRunStore(Path.Combine(evidence,"workspace"));var coordinator=new RunCoordinator(store,new HttpTransport());var run=await coordinator.StartAsync(package,url);
            Task<Attempt> Send(TestCase test,IdentitySession identity)=>coordinator.ExecuteAsync(run,test,test.Request,test.Assertions,identity,default);
            await Send(package.Cases[0],admin);await Send(package.Cases[1],admin);
            await Measure("Create",()=>Send(create,admin));var duplicate=await Measure("Duplicate create",()=>Send(create,admin));
            check(duplicate.Verdict==Verdict.Fail&&duplicate.Response!.StatusCode==409,"Measured journey records duplicate create as failed");
            var resolved=GuidedActionBindings.Resolve(cleanup.Request,run);
            check(resolved.Url=="/items/1","Measured journey cleanup selects original successful resource after duplicate failure");
            // Execute the same resolved action through transport; retain the response in measured evidence.
            var deleted=await Measure("Guided cleanup",()=>new HttpTransport().SendAsync(resolved,RequestUrls.Resolve(url,resolved),admin,default));
            await Reports.WriteAtomicAsync(Path.Combine(evidence,"cleanup.json"),PackageJson.Write(deleted));
            check(deleted.StatusCode==204&&Convert.ToInt64(await Sql("SELECT count(*) FROM items"))==0,"Measured journey cleanup removes the row through HTTP");
            await Measure("Retry create",()=>Send(create,admin));
            var target=new DatabasePreparationTarget(new("db",DatabaseProvider.Sqlite,connectionString));var recovery=new PreparationRecovery { Plan=plan,OriginalJson=await target.ReadAsync(plan,default),RunId=run.Id,CaseId=denied.Id };
            Task Save()=>Reports.WriteAtomicAsync(Path.Combine(evidence,"recovery.json"),PackageJson.Write(recovery));
            await Preparations.ApplyAsync(recovery,target,Save,default);run.PreparationLog=[Preparations.Audit(recovery)];
            var reader=new IdentitySession("cloud-reader",AuthKind.Bearer,await Measure("Sign in as prepared role",()=>client.GetStringAsync(url+"/login")));
            check(PermissionIdentity.Matches(plan,reader),"Measured journey new JWT contains prepared permissions");
            var permission=await Measure("Verify reader denied",()=>Send(denied,reader));check(permission.Verdict==Verdict.Pass,"Measured journey validates role denial");
            await Measure("Reuse reader JWT",()=>Send(denied,reader));
            await Preparations.RestoreAsync(recovery,target,Save,default);run.PreparationLog.Add(Preparations.Audit(recovery));
            check(await target.ReadAsync(plan,default)=="15"&&PermissionIdentity.Matches(plan,reader),"Measured journey restores database and retains issued role JWT");
            run.FinishedAt=DateTimeOffset.UtcNow;await coordinator.SaveAsync(run);
            await Reports.WriteAtomicAsync(Path.Combine(evidence,"report.json"),Reports.Json(run));await Reports.WriteAtomicAsync(Path.Combine(evidence,"report.html"),Reports.Html(run));
            await Reports.WriteAtomicAsync(Path.Combine(evidence,"timings.json"),PackageJson.Write(new { environment="Loopback HTTP + disposable SQLite + synthetic token issuer; no human/MFA timing",totalMs=timer.ElapsedMilliseconds,steps=timings }));
            check(Outcomes.ForRun(run)==Verdict.Pass&&run.Attempts.Any(a=>a.Verdict==Verdict.Fail),"Measured journey final report passes while retaining the failed duplicate attempt");
        }
        finally { listener.Stop();await serving; }
    }
}
