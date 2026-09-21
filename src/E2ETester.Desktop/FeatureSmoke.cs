using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using E2ETester.Application;
using E2ETester.Core;
namespace E2ETester.Desktop;
internal static class FeatureSmoke
{
    public static async Task RunAsync(MainWindow window,MainViewModel model,string path)
    {
        await UpdateSettingsSmoke.RunAsync(window,path);
        await CheckNavigationAsync(window, path);
        if (ResponsePreferences.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "responses.json")).RedactSensitiveValues)
            throw new InvalidOperationException("Response redaction must default to off.");
        _ = window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var settings = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Response settings");
            Save(settings, path + ".response-settings.png");
            var toggle = Descendants(settings).OfType<CheckBox>().Single(); toggle.IsChecked = false;
            Descendants(settings).OfType<Button>().Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.ApplicationIdle);
        model.OpenResponseSettings.Execute(null);
        _ = window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var settings = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Response settings");
            var toggle = Descendants(settings).OfType<CheckBox>().Single();
            if (toggle.IsChecked != false) throw new InvalidOperationException("Response setting was not saved.");
            toggle.IsChecked = true;
            Descendants(settings).OfType<Button>().Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.ApplicationIdle);
        model.OpenResponseSettings.Execute(null);
        var file=Path.Combine(Path.GetTempPath(),"package with spaces-"+Guid.NewGuid().ToString("N")+".jsee");
        await File.WriteAllTextAsync(file,AgentResources.Template);model.OpenPackageFile(file);
        if(model.Cases.Count!=2 || model.SelectedCase?.Baseline.Kind!=TestStepKind.SwaggerAuthentication)throw new InvalidOperationException(".jsee package did not open its setup flow.");
        model.BaseUrl = "https://gateway.example.test";
        model.Cases[0].Url = "/custom/swagger/";
        await model.SaveLibrary.RunAsync();
        model.SelectedLibrary = model.Library.First();
        model.SwaggerUrl = "https://stale.example.test/swagger/";
        await model.LoadLibrary.RunAsync();
        if (model.SwaggerUrl != "https://gateway.example.test/custom/swagger/") throw new InvalidOperationException("Shared package loading kept the previous Swagger URL.");
        model.BaseUrl = "https://second.example.test";
        if (model.SwaggerUrl != "https://second.example.test/custom/swagger/") throw new InvalidOperationException("Swagger URL did not follow the environment.");
        model.Cases[0].Url = "https://login.example.test/swagger/";
        await model.SaveLibrary.RunAsync();
        model.SelectedLibrary = model.Library.First();
        model.SwaggerUrl = "https://stale.example.test/swagger/";
        await model.LoadLibrary.RunAsync();
        if (model.SwaggerUrl != "https://login.example.test/swagger/") throw new InvalidOperationException("Absolute Swagger URL was not preserved.");
        model.OpenPackageFile(file);
        if(!PackageAssociation.Command(@"C:\Program Files\E2E Tester.exe").Contains("\"%1\""))throw new InvalidOperationException("Association argument must be quoted.");
        model.SelectedIdentity!.Secret="local-ui-test-token";await model.Send.RunAsync();
        if(model.SelectedAttempt?.Verdict!=Verdict.Pass || model.SelectedAttempt.Response is not null)throw new InvalidOperationException("Authentication-only UI sent HTTP or did not record success.");
        _ = window.Dispatcher.BeginInvoke(new Action(async () =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<ManualAssessmentWindow>().Single();
            var editor = (AssessmentEditor)dialog.DataContext;
            if (editor.Verdict != Verdict.Fail) throw new InvalidOperationException("Mark as failed must preselect FAIL.");
            editor.Reviewer = "UI tester"; editor.Reason = "Data does not match the expected result.";
            await editor.Record.RunAsync(); dialog.Close();
        }), DispatcherPriority.ApplicationIdle);
        model.MarkAttemptFailed.Execute(null);
        if (model.SelectedAttempt.Verdict != Verdict.Pass || !model.HasFailureVerdict || !model.SelectedCase!.Status.Contains("MANUAL: FAIL"))
            throw new InvalidOperationException("Manual FAIL must be visible beside automated PASS.");
        Save(window, path + ".manual-fail.png");
        bool inspected=false;
        _ = window.Dispatcher.BeginInvoke(new Action(()=>
        {
            var report=System.Windows.Application.Current.Windows.OfType<TextDocumentWindow>().Single();
            var tabs=((DockPanel)report.Content).Children.OfType<TabControl>().Single();
            if(tabs.Items.Count==2 && ((TextBox)((TabItem)tabs.Items[0]).Content).IsReadOnly)inspected=true;
            Save(report,path+".report.png");report.Close();
        }),DispatcherPriority.ApplicationIdle);
        model.ViewReport.Execute(null);
        if(!inspected)throw new InvalidOperationException("Readable/JSON report modal missing.");
        var help=new TextDocumentWindow("AI test generator",("Prompt",AgentResources.Prompt),("Template",AgentResources.Template)){Owner=window};help.Show();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Save(help,path+".help.png");help.Close();
        var importer=new OpenApiWindow(model.BaseUrl){Owner=window};importer.Show();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Save(importer,path+".openapi.png");importer.Close();
        if (!window.Title.Contains(AppInfo.VersionLabel)) throw new InvalidOperationException("Window title lacks the running version.");
        var about = new AboutWindow { Owner=window }; about.Show();
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        if (!AppInfo.ReadNotice("LICENSE").Contains("d3sync") || !AppInfo.Details.Contains("https://d3sync.org")) throw new InvalidOperationException("About credits or license missing.");
        Save(about,path+".about.png");
        var aboutTabs = ((DockPanel)about.Content).Children.OfType<TabControl>().Single();
        aboutTabs.SelectedIndex = 2;
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Save(about,path+".notices.png"); about.Close();
        var sessions = Path.Combine(Path.GetTempPath(),"remembered-session-test-"+Guid.NewGuid().ToString("N"));
        string profileKey;
        using (var first = new MainViewModel(Path.Combine(sessions,"workspace-a"),sessions))
        {
            first.LoadNewSuite(); first.NewIdentity.Execute(null);
            first.SelectedIdentity!.Secret="remembered-canary-663";
            profileKey=first.SelectedIdentity.BrowserProfileKey;
            await first.Send.RunAsync();
            await first.ChangeIdentityAsync(first.SelectedIdentity,rename:"returning-user");
        }
        using (var second = new MainViewModel(Path.Combine(sessions,"workspace-b"),sessions))
        {
            second.LoadNewSuite();
            if(second.SelectedIdentity?.Alias!="returning-user" || second.SelectedIdentity.Secret!="remembered-canary-663" || second.SelectedIdentity.BrowserProfileKey!=profileKey)
                throw new InvalidOperationException("Session or browser profile did not survive workspace restart and rename.");
            second.BaseUrl="https://unrelated.example";
            if(second.SelectedIdentity.Secret.Length!=0)throw new InvalidOperationException("Remembered token crossed server origins.");
            second.BaseUrl="https://api.example.test";
            if(second.SelectedIdentity.Secret!="remembered-canary-663")throw new InvalidOperationException("Returning to the server did not restore its token.");
            await second.ChangeIdentityAsync(second.SelectedIdentity,clearToken:true);
        }
        using (var third = new MainViewModel(Path.Combine(sessions,"workspace-c"),sessions))
        {
            third.LoadNewSuite(); var restored=third.Identities.Single(i=>i.Alias=="returning-user");
            if(restored.Secret.Length!=0 || restored.BrowserProfileKey!=profileKey)throw new InvalidOperationException("Clearing token lost browser mapping or restored credentials.");
            await third.ChangeIdentityAsync(restored);
        }
        if(new RememberedSessions(sessions).Read().Count!=0)throw new InvalidOperationException("Deleted identity remained remembered.");
        using (var savedModel = new MainViewModel(Path.Combine(Path.GetTempPath(),"identity-profile-test-"+Guid.NewGuid().ToString("N"))))
        {
            var savedIdentity = savedModel.CreateIdentity("saved-user"); savedIdentity.Secret="synthetic-saved-token";
            savedModel.SelectedIdentity=savedIdentity;
            await savedModel.SaveEnvironment.RunAsync();
            await savedModel.ChangeIdentityAsync(savedIdentity,rename:"saved-renamed");
            await savedModel.LoadEnvironment.RunAsync();
            var renamed = savedModel.Identities.Single(i=>i.Alias=="saved-renamed");
            if (renamed.Secret!="synthetic-saved-token" || savedModel.Identities.Any(i=>i.Alias=="saved-user")) throw new InvalidOperationException("Saved environment rename failed.");
            await savedModel.ChangeIdentityAsync(renamed,clearToken:true);
            await savedModel.LoadEnvironment.RunAsync();
            renamed=savedModel.Identities.Single(i=>i.Alias=="saved-renamed");
            if(renamed.Secret.Length!=0)throw new InvalidOperationException("Saved token was not cleared.");
            await MustReject(()=>savedModel.ChangeIdentityAsync(renamed));
            await MustReject(()=>savedModel.ChangeIdentityAsync(savedModel.Identities[0],rename:"forbidden"));
            await MustReject(()=>Task.FromResult(savedModel.CreateIdentity("SAVED-RENAMED")));
            await savedModel.ChangeIdentityAsync(renamed,replacement:savedModel.Identities[0]);
            await savedModel.LoadEnvironment.RunAsync();
            if(savedModel.Identities.Any(i=>i.Alias=="saved-renamed"))throw new InvalidOperationException("Saved environment resurrected a deleted identity.");
        }
        var original = model.SelectedIdentity!; var originalAlias = original.Alias;
        await model.ChangeIdentityAsync(original, rename:"renamed-user");
        if (model.Cases.Any(c=>c.IdentityRef==originalAlias) || model.SelectedAttempt!.IdentityAlias!=originalAlias) throw new InvalidOperationException("Rename lost references or changed history.");
        var replacement = model.CreateIdentity("replacement-user"); replacement.Secret="local-replacement-token";
        await model.ChangeIdentityAsync(original,replacement:replacement);
        if (model.Identities.Contains(original) || original.Secret.Length!=0 || model.Cases.Any(c=>c.IdentityRef=="renamed-user")) throw new InvalidOperationException("Delete did not clear identity and update references.");
        await model.Send.RunAsync();
        if (model.SelectedAttempt?.IdentityAlias!="replacement-user" || model.SelectedAttempt.Verdict!=Verdict.Pass) throw new InvalidOperationException("Replacement identity cannot complete sign-in.");
        await model.RestoreDraft.RunAsync();
        if (model.Identities.Any(i=>i.Alias==originalAlias || i.Alias=="renamed-user")) throw new InvalidOperationException("Recovery resurrected a deleted identity.");
        var manager = new IdentityManagerWindow(model) { Owner=window }; manager.Show();
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); Save(manager,path+".identities.png"); manager.Close();
        window.RequestTabs.SelectedIndex=3;
        _ = window.Dispatcher.BeginInvoke(new Action(()=>
        {
            var agent=System.Windows.Application.Current.Windows.OfType<TextDocumentWindow>().Single();
            if(((DockPanel)agent.Content).Children.OfType<TabControl>().Single().Items.Count!=4)throw new InvalidOperationException("AI assistant resources missing.");
            Save(agent,path+".agent.png");agent.Close();
        }),DispatcherPriority.ApplicationIdle);
        model.AgentHelp.Execute(null);
        var kit=Path.Combine(Path.GetTempPath(),"agent-kit-smoke-"+Guid.NewGuid().ToString("N"));AgentResources.ExportKit(kit);
        if(!File.Exists(Path.Combine(kit,"e2e-tester-author","SKILL.md")) || !File.Exists(Path.Combine(kit,"e2e-tester-author","references","test-package.v1.4.schema.json")))throw new InvalidOperationException("Exported skill incomplete.");
        var toolsButton=Descendants(window).OfType<Button>().Single(b=>b.ContextMenu is not null);
        toolsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        if(!toolsButton.ContextMenu.IsOpen || toolsButton.ContextMenu.DataContext!=model)throw new InvalidOperationException("Tools menu not connected.");
        Save(toolsButton.ContextMenu,path+".menu.png");toolsButton.ContextMenu.IsOpen=false;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static async Task CheckNavigationAsync(MainWindow owner, string path)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "navigation-smoke-" + Guid.NewGuid().ToString("N"));
        var package = SuiteDefaults.Create();
        var run = new TestRun { Package = package, BaseUrl = package.BaseUrl };
        run.Attempts = package.Cases.Select((c, i) => new Attempt
        {
            CaseId = c.Id, CaseTitle = c.Title, State = AttemptState.Completed,
            Verdict = i == 0 ? Verdict.Fail : Verdict.Inconclusive, FinishedAt = DateTimeOffset.UtcNow
        }).ToList();
        using (var store = new E2ETester.Infrastructure.SqliteRunStore(workspace)) await store.SaveAsync(run);
        using var model = new MainViewModel(workspace);
        var view = new MainWindow(model, restoreDraft: false) { Owner = owner };
        view.Show();
        try
        {
            await model.RestoreRunAsync(run.Id);
            await view.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ApplicationIdle);
            if (!model.AllCasesCompleted || Grid.GetColumn(view.FinishRunButton) != 4
                || view.FinishRunButton.Background != System.Windows.Application.Current.Resources["Accent"])
                throw new InvalidOperationException("Completed cases did not promote Finish run.");
            Save(view, path + ".finish-primary.png");
            await model.AddVariation.RunAsync();
            await view.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ApplicationIdle);
            if (model.AllCasesCompleted || Grid.GetColumn(view.NextCaseButton) != 4
                || view.NextCaseButton.Background != System.Windows.Application.Current.Resources["Accent"])
                throw new InvalidOperationException("An untested variation did not restore Next case as primary.");
            Save(view, path + ".next-primary.png");
            for (var i = 0; i < 30; i++) model.Cases.Add(new CaseEditor(new TestCase { Title = "Scroll test " + i }));
            model.SelectedCase = model.Cases[^2];
            await view.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ContextIdle);
            model.Next.Execute(null);
            await view.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ContextIdle);
            var item = (ListBoxItem)view.CaseList.ItemContainerGenerator.ContainerFromItem(model.SelectedCase);
            var position = item.TransformToAncestor(view.CaseList).Transform(new Point());
            if (position.Y < 0 || position.Y + item.ActualHeight > view.CaseList.ActualHeight + 1)
                throw new InvalidOperationException("Next case did not scroll the selected item into view.");
        }
        finally { view.DataContext = null; view.Close(); }
    }
    private static void Save(FrameworkElement window,string path)
    {
        window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);
    }
    private static async Task MustReject(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid identity operation was accepted.");
    }
}


