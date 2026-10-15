using System.Text.Json;
using System.Xml.Linq;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;
public static class HeadlessRunner
{
    public static async Task<TestRun> RunAsync(TestPackage package,AgentConfiguration config,string baseUrl,string output,CancellationToken token)
    {
        await using var workspace=new AgentWorkspace(config);
        var run=await workspace.StartAsync(PackageJson.Write(package),baseUrl);
        var issues=new Dictionary<string,string>();string? finishError=null;
        try
        {
            foreach(var test in package.Cases)
            {
                if(token.IsCancellationRequested) { issues[test.Id]="Execution cancelled."; break; }
                try
                {
                    if(test.Preparation is not null && config.AllowPreparation)
                    {
                        await workspace.RestoreEnvironmentAsync(run.Id,token);
                        await workspace.PrepareEnvironmentAsync(run.Id,test.Id,Guid.NewGuid().ToString("N"),token);
                    }
                    string? fixture=null;
                    if(test.FixtureRequired || ValueBindings.RequiresFixture(test.Request)) fixture=(await workspace.QueryAsync(run.Id,test.FixtureQueryId,token)).Id;
                    var prepared=JsonSerializer.SerializeToElement(await workspace.PrepareAsync(run.Id,test.Id,fixtureEvidenceId:fixture),PackageJson.Options);
                    var id=prepared.GetProperty("executionId").GetString()!;
                    await workspace.ExecuteAsync(id);
                    while(true)
                    {
                        if(token.IsCancellationRequested) workspace.Cancel(id);
                        var state=JsonSerializer.SerializeToElement(await workspace.OperationAsync(id),PackageJson.Options).GetProperty("state").GetString();
                        if(state!="running") { if(state!="finished") issues[test.Id]="Execution "+state+". Inspect captured evidence; no retry was attempted.";break; }
                        await Task.Delay(50,CancellationToken.None);
                    }
                    run=await workspace.GetRunAsync(run.Id);
                    if(run.Attempts.LastOrDefault(a=>a.CaseId==test.Id) is {} attempt && test.DatabaseChecks.Count>0) await workspace.VerifyAsync(run.Id,attempt.Id,token);
                }
                catch(Exception ex) { issues[test.Id]=ex.Message; }
                if(issues.TryGetValue(test.Id,out var issue)) await workspace.RecordExecutionIssueAsync(run.Id,test.Id,issue);
            }
        }
        finally
        {
            try { await workspace.FinishAsync(run.Id); } catch(Exception ex) { finishError=ex.Message; }
            run=await workspace.GetRunAsync(run.Id);
            if(issues.Count>0 || finishError is not null) run.FinishedAt=null;
            Directory.CreateDirectory(output);
            await Reports.WriteAtomicAsync(Path.Combine(output,"report.json"),Reports.Json(run));
            await Reports.WriteAtomicAsync(Path.Combine(output,"report.html"),Reports.Html(run));
            await Reports.WriteAtomicAsync(Path.Combine(output,"diagnostics.json"),PackageJson.Write(new { issues,finishError }));
            await Reports.WriteAtomicAsync(Path.Combine(output,"junit.xml"),JUnit(run,issues,finishError));
        }
        // An operation error must not disappear behind an earlier passing HTTP attempt.
        if(issues.Count>0 || finishError is not null) run.FinishedAt=null;
        return run;
    }
    public static string JUnit(TestRun run,IReadOnlyDictionary<string,string> issues,string? finishError)
    {
        var suite=new XElement("testsuite",new XAttribute("name",run.Package.Title));int failed=0,errors=0;
        foreach(var test in run.Package.Cases)
        {
            var attempt=run.Attempts.LastOrDefault(a=>a.CaseId==test.Id);
            var element=new XElement("testcase",new XAttribute("name",test.Title),new XAttribute("classname",run.Package.Id),new XAttribute("time",((attempt?.Response?.DurationMs??0)/1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if(issues.TryGetValue(test.Id,out var issue)){element.Add(new XElement("error",new XAttribute("message",issue)));errors++;}
            else if(Outcomes.ForAttempt(attempt)==Verdict.Fail){element.Add(new XElement("failure",new XAttribute("message","FAIL"),string.Join("\n",attempt!.Checks.Where(c=>c.Verdict==Verdict.Fail).Select(c=>c.Message))));failed++;}
            else if(Outcomes.ForAttempt(attempt)!=Verdict.Pass){element.Add(new XElement("error",new XAttribute("message","INCONCLUSIVE: missing or incomplete evidence")));errors++;}
            suite.Add(element);
        }
        if(finishError is not null){suite.Add(new XElement("testcase",new XAttribute("name","Restore and finish"),new XElement("error",finishError)));errors++;}
        suite.SetAttributeValue("tests",suite.Elements("testcase").Count());suite.SetAttributeValue("failures",failed);suite.SetAttributeValue("errors",errors);
        return new XDocument(new XElement("testsuites",suite)).ToString();
    }
}
