using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using E2ETester.Application;
using Microsoft.Win32;
namespace E2ETester.Desktop;
public sealed class OpenApiWindow : Window
{
    public E2ETester.Core.TestPackage? ImportedPackage { get; private set; }
    public ApiOperation? Selected { get; private set; }
    public OpenApiWindow(string baseUrl)
    {
        Title="Import OpenAPI / Postman"; Width=880; Height=650; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel { Margin=new Thickness(20) }; var top=new StackPanel();
        top.Children.Add(new TextBlock { Text="Load an OpenAPI JSON/YAML specification or a Postman v2.1 collection. Select a reference folder for split specifications. Review warnings before importing. Nothing is sent.", TextWrapping=TextWrapping.Wrap });
        var address=new TextBox { Text=baseUrl.TrimEnd('/')+"/swagger/v1/swagger.json", Margin=new Thickness(0,12,0,8) }; top.Children.Add(address);
        var actions=new StackPanel { Orientation=Orientation.Horizontal }; var load=new Button { Content="Load URL" };var file=new Button { Content="Open file" }; var references=new Button { Content="Reference folder…" }; Dictionary<string,string>? documents=null; references.Click+=(_,_)=>{var folder=new OpenFolderDialog();if(folder.ShowDialog()==true) { try { documents=ImportFiles.References(folder.FolderName); references.Content="References loaded"; } catch(Exception ex) { MessageBox.Show(ex.Message); } }}; actions.Children.Add(load);actions.Children.Add(file);actions.Children.Add(references);top.Children.Add(actions);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,12) };top.Children.Add(new ScrollViewer { Content=status,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
        var list=new ListBox { DisplayMemberPath="Label" };var choose=new Button { Content="Open selected request", IsEnabled=false, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };DockPanel.SetDock(choose,Dock.Bottom);dock.Children.Add(choose);dock.Children.Add(list);
        E2ETester.Core.TestPackage? collection=null;
        var importAll=new Button { Content="Import all as a test package",IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Left };DockPanel.SetDock(importAll,Dock.Bottom);dock.Children.Insert(1,importAll);
        list.SelectionChanged+=(_,_)=>choose.IsEnabled=list.SelectedItem is ApiOperation;
        importAll.Click+=(_,_)=> { if(collection is not null) { ImportedPackage=collection;DialogResult=true; } };
        void Parse(string text) {
            collection=null; list.ItemsSource=null; importAll.IsEnabled=false; choose.IsEnabled=false;
            using var json=System.Text.Json.JsonDocument.Parse(SpecificationDocument.Normalize(text));
            if(json.RootElement.TryGetProperty("info",out var info)&&info.TryGetProperty("schema",out _)) {
                var imported=PostmanImport.Parse(text,baseUrl);collection=imported.Package;
                status.Text="Review: scripts are not translated; add assertions. " + string.Join("\n",imported.Warnings);
                collection.Description += "\n" + string.Join("\n",imported.Warnings);
                list.ItemsSource=collection.Cases.Select(c=>new ApiOperation(c.Title,baseUrl,c)).ToList();
            } else {
                var operations=OpenApiImport.Parse(text,baseUrl,documents);list.ItemsSource=operations;
                collection=new() { Title="Imported OpenAPI",BaseUrl=baseUrl,Cases=operations.Select(o=> { var c=PackageJson.Clone(o.Case);c.Request.Url=RequestUrls.Resolve(o.BaseUrl,c.Request).AbsoluteUri;return c; }).ToList() };
                status.Text="Select an operation or import all. Review examples, authentication and path values before sending.";
            }
            importAll.IsEnabled=collection.Cases.Count>0;
        }
        load.Click+=async(_,_)=>
        {
            load.IsEnabled=file.IsEnabled=false;
            try
            {
                if(!Uri.TryCreate(address.Text,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length>0)throw new InvalidDataException("Use an HTTP(S) specification URL without credentials.");
                using var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) { Timeout=TimeSpan.FromSeconds(30),MaxResponseContentBufferSize=5_000_000 };
                Parse(await client.GetStringAsync(uri));
            } catch(Exception ex) {status.Text="Could not import: "+ex.Message+" Use the specification URL, or save the spec in Swagger and open the file.";}
            finally {load.IsEnabled=file.IsEnabled=true;}
        };
        file.Click+=(_,_)=> { try {var picker=new OpenFileDialog { Filter="Specifications / collections|*.json;*.yaml;*.yml" };if(picker.ShowDialog()==true){if(new FileInfo(picker.FileName).Length>5_000_000)throw new InvalidDataException("Document exceeds 5 MB.");Parse(File.ReadAllText(picker.FileName));}}catch(Exception ex){status.Text=ex.Message;} };
        choose.Click+=(_,_)=> { if(list.SelectedItem is ApiOperation operation){Selected=operation;DialogResult=true;} };Content=dock;
    }
}
