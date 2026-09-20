using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using E2ETester.Application;
using Microsoft.Win32;
namespace E2ETester.Desktop;
public sealed class OpenApiWindow : Window
{
    public ApiOperation? Selected { get; private set; }
    public OpenApiWindow(string baseUrl)
    {
        Title="Import an OpenAPI request"; Width=880; Height=650; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel { Margin=new Thickness(20) }; var top=new StackPanel();
        top.Children.Add(new TextBlock { Text="Paste the OpenAPI JSON URL from Swagger, or choose a local JSON file. Import never sends an API operation.", TextWrapping=TextWrapping.Wrap });
        var address=new TextBox { Text=baseUrl.TrimEnd('/')+"/swagger/v1/swagger.json", Margin=new Thickness(0,12,0,8) }; top.Children.Add(address);
        var actions=new StackPanel { Orientation=Orientation.Horizontal }; var load=new Button { Content="Load URL" };var file=new Button { Content="Open JSON file" }; actions.Children.Add(load);actions.Children.Add(file);top.Children.Add(actions);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,12) };top.Children.Add(status);DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
        var list=new ListBox { DisplayMemberPath="Label" };var choose=new Button { Content="Open selected request", HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };DockPanel.SetDock(choose,Dock.Bottom);dock.Children.Add(choose);dock.Children.Add(list);
        void Parse(string text) { list.ItemsSource=OpenApiImport.Parse(text,baseUrl);status.Text="Select an operation. Review generated examples and path values before sending."; }
        load.Click+=async(_,_)=>
        {
            load.IsEnabled=file.IsEnabled=false;
            try
            {
                if(!Uri.TryCreate(address.Text,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length>0)throw new InvalidDataException("Use an HTTP(S) specification URL without credentials.");
                using var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) { Timeout=TimeSpan.FromSeconds(30),MaxResponseContentBufferSize=5_000_000 };
                Parse(await client.GetStringAsync(uri));
            } catch(Exception ex) {status.Text="Could not import: "+ex.Message+" Use the JSON spec URL, or save the spec in Swagger and open the file.";}
            finally {load.IsEnabled=file.IsEnabled=true;}
        };
        file.Click+=(_,_)=> { try {var picker=new OpenFileDialog { Filter="OpenAPI JSON|*.json" };if(picker.ShowDialog()==true){if(new FileInfo(picker.FileName).Length>5_000_000)throw new InvalidDataException("Document exceeds 5 MB.");Parse(File.ReadAllText(picker.FileName));}}catch(Exception ex){status.Text=ex.Message;} };
        choose.Click+=(_,_)=> { if(list.SelectedItem is ApiOperation operation){Selected=operation;DialogResult=true;} };Content=dock;
    }
}
