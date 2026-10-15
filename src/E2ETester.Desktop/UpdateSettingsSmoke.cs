using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace E2ETester.Desktop;
internal static class UpdateSettingsSmoke
{
    internal static async Task RunAsync(Window owner,string screenshot)
    {
        var root=Path.Combine(Path.GetTempPath(),"update-settings-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var feed=Path.Combine(root,"feed");Directory.CreateDirectory(feed);
        await File.WriteAllTextAsync(Path.Combine(feed,"releases.win.json"),"""
        {"Assets":[{"PackageId":"E2ETester","Version":"99.0.0","Type":"Full","FileName":"future.nupkg","SHA1":"0000000000000000000000000000000000000000","Size":1},{"PackageId":"E2ETester","Version":"0.0.1","Type":"Full","FileName":"old.nupkg","SHA1":"0000000000000000000000000000000000000000","Size":1}]}
        """);
        var settings=Path.Combine(root,"settings");
        if(!UpdatePreferences.SourceCandidates(UpdatePreferences.PrimarySource).SequenceEqual([UpdatePreferences.PrimarySource,UpdatePreferences.MirrorSource]))throw new InvalidOperationException("Primary update feed did not include its mirror fallback.");
        if(!UpdatePreferences.SourceCandidates("https://updates.example.test/releases/").SequenceEqual(["https://updates.example.test/releases/"]))throw new InvalidOperationException("Custom update feed unexpectedly gained a mirror.");
        var model=new UpdatesViewModel(()=>throw new InvalidOperationException("Checking must never apply an update."),settings){Source=feed,CheckOnStartup=true};
        model.SaveSettings.Execute(null);
        var reopened=new UpdatesViewModel(()=>Task.CompletedTask,settings);
        if(reopened.Source!=feed || !await reopened.CheckAtStartupAsync() || reopened.NewestVersion!="99.0.0")throw new InvalidOperationException("Startup update detection/settings recovery failed.");
        reopened.CheckOnStartup=false;reopened.SaveSettings.Execute(null);
        if(await new UpdatesViewModel(()=>Task.CompletedTask,settings).CheckAtStartupAsync())throw new InvalidOperationException("Disabled startup checks ran.");
        await File.WriteAllTextAsync(Path.Combine(feed,"releases.win.json"),"""{"Assets":[{"PackageId":"E2ETester","Version":"0.0.1","Type":"Full","FileName":"old.nupkg","SHA1":"0000000000000000000000000000000000000000","Size":1}]}""");
        reopened.CheckOnStartup=true;
        if(await reopened.CheckAtStartupAsync() || !reopened.Status.Contains("up to date"))throw new InvalidOperationException("Older feed prompted an upgrade.");
        var window=new UpdatesWindow(reopened){Owner=owner};window.Show();
        await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(screenshot+".updates.png"))encoder.Save(stream);
        while(reopened.Busy)await Task.Delay(20);window.Close();
        reopened.Source=Path.Combine(root,"missing");if(await reopened.CheckAtStartupAsync())throw new InvalidOperationException("Missing feed produced update notification.");
    }
}
