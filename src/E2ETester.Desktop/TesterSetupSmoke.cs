using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Desktop;
internal static class TesterSetupSmoke
{
    internal static async Task RunAsync(Window owner,string path)
    {
        var directory=Path.Combine(Path.GetTempPath(),"E2E-tester-setup",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var connectionString=new SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"setup.db"),Pooling=false }.ToString();
        using(var connection=new SqliteConnection(connectionString)){connection.Open();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE accounts(ID TEXT PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES('tester-b',31)";command.ExecuteNonQuery();}
        using var port=new TcpListener(IPAddress.Loopback,0);port.Start();var number=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();var url=$"http://127.0.0.1:{number}";
        using var server=new HttpListener();server.Prefixes.Add(url+"/");server.Start();
        var plan=new PreparationPlan { Provider=DatabaseProvider.Sqlite,ConnectionRef="admin",Table="accounts",KeyColumn="ID",KeyJson="\"{{identity.cloud-user.userid}}\"",ValueColumn="permissions",ValueJson="4",RequireFreshLogin=true,JwtClaim="permissions" };
        var package=new TestPackage { SchemaVersion="1.8",BaseUrl=url,Title="Reusable tester setup",Cases=[new() { Id="role",Title="Reader with fixtures from group 1",Preparation=plan,Request=new() { Url="/read",IdentityRef="cloud-reader" },Assertions=[new()],AppliesWhen=[new("cloud-user","groupid","1","Fixture group")] }] };
        var file=Path.Combine(directory,"setup.jsee");File.WriteAllText(file,PackageJson.Write(package));
        using var model=new MainViewModel(Path.Combine(directory,"workspace"));model.OpenPackageFile(file);
        model.Database.Connections.Add(new() { Alias="admin",Provider=DatabaseProvider.Sqlite,ConnectionString=connectionString });
        string Jwt(int permissions)=>"e30."+Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { userid="tester-b",permissions,groupid=24,exp=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }))).TrimEnd('=').Replace('+','-').Replace('/','_')+".test";
        model.Identities.Single(i=>i.Alias=="cloud-user").Secret=Jwt(31);model.Identities.Single(i=>i.Alias=="cloud-reader").Secret=Jwt(4);
        var preview=await model.TesterSetupPreviewAsync();
        if(!preview.Contains("tester-b")||!preview.Contains("Original value for restoration: 31")||!preview.Contains("CHECK APPLICABILITY"))throw new InvalidOperationException("Tester setup preview lost target, original value or applicability warning.");
        if(model.HasPendingPreparation)throw new InvalidOperationException("Read-only setup review created a preparation.");
        var dialog=new TextDocumentWindow("Review tester setup",("Account and applicability",preview)) { Owner=owner };dialog.Show();await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path+".tester-setup.png"))encoder.Save(stream);dialog.Close();
        model.SelectedCase=model.Cases.Single(c=>c.Baseline.Id=="role");await model.Send.RunAsync();
        if(!model.Message.Contains("may not apply"))throw new InvalidOperationException("Mismatched tester was not flagged before execution.");
        model.RunAnyway=true;model.RunAnywayConfirmation=()=>true;
        var receive=server.GetContextAsync();var sending=model.Send.RunAsync();var context=await receive.WaitAsync(TimeSpan.FromSeconds(10));context.Response.StatusCode=200;context.Response.ContentLength64=0;context.Response.Close();await sending;
        if(model.SelectedAttempt?.Verdict!=Verdict.Pass||!model.SelectedAttempt.Changes.Any(c=>c.Path=="execution.applicability"))throw new InvalidOperationException("Explicit applicability override was blocked or not audited: "+model.Message);
        var source=model.Identities.Single(i=>i.Alias=="cloud-user");await model.ChangeIdentityAsync(source,rename:"tester-source");
        var selected=model.Cases.Single(c=>c.Baseline.Id=="role").Baseline;
        if(IdentityBindings.KeyBinding(selected.Preparation!)?.Alias!="tester-source"||selected.AppliesWhen![0].IdentityRef!="tester-source")throw new InvalidOperationException("Renaming source identity left stale tester setup references.");
    }
}
