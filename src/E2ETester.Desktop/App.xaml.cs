using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace E2ETester.Desktop;

public partial class App : System.Windows.Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        if (args.Contains("--register-file-association")) { PackageAssociation.Register(); return; }
        var app = new App(); app.InitializeComponent(); app.Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var smoke = e.Args.FirstOrDefault(a => a.StartsWith("--smoke="));
            ThemeManager.Initialize(smoke is null);
            if (e.Args.Contains("--dark")) ThemeManager.Apply(true, false);
            var workspace = smoke is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2ETester", "Workspaces", "default") : Path.Combine(Path.GetTempPath(), "E2ETester-smoke", Guid.NewGuid().ToString("N"));
            TextWriterTraceListener? listener = null;
            if (smoke is not null)
            {
                var logPath = Path.GetFullPath(smoke[8..]); Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                listener = new TextWriterTraceListener(logPath + ".bindings.log");
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            }
            if (smoke is not null && e.Args.Contains("--history-exercise"))
            {
                using var seed = new Infrastructure.SqliteRunStore(workspace);
                seed.SaveAsync(new Core.TestRun { Id = "finished-history-demo", Package = MainViewModel.Demo(), BaseUrl = "http://localhost:5188", StartedAt = DateTimeOffset.UtcNow.AddDays(-1), FinishedAt = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(5) }).GetAwaiter().GetResult();
                seed.SaveAsync(new Core.TestRun { Id = "unfinished-history-demo", Package = MainViewModel.DatabaseDemoPackage(), BaseUrl = "http://localhost:5188" }).GetAwaiter().GetResult();
            }
            if (smoke is null && e.Args.FirstOrDefault(a => a.StartsWith("--workspace=")) is { } workspaceArgument) workspace = Path.GetFullPath(workspaceArgument[12..]);
            var packageFile = e.Args.FirstOrDefault(a => !a.StartsWith("--") && Path.GetExtension(a).Equals(".jsee", StringComparison.OrdinalIgnoreCase));
            if (packageFile is not null && smoke is null)
            {
                packageFile=Path.GetFullPath(packageFile);
                var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(packageFile.ToUpperInvariant())))[..20];
                workspace=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"E2ETester","Workspaces","packages",hash);
            }
            var model = new MainViewModel(workspace, smoke is null ? RememberedSessions.DefaultDirectory : null);
            if (smoke is null)
            {
                model.LoadNewSuite();
                try { model.Message=PackageAssociation.Register(); } catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { model.Message="File association could not be registered. You can still open packages from the app."; }

            }
            if(packageFile is not null)model.OpenPackageFile(packageFile);
            if (e.Args.Contains("--auth-check") || e.Args.Contains("--gateway-exercise")) model.LoadGatewayAuthCheck();
            var window = new MainWindow(model, smoke is null, !e.Args.Contains("--auth-check") && packageFile is null); MainWindow = window;
            if (smoke is not null)
            {
                var path = Path.GetFullPath(smoke[8..]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                window.ShowInTaskbar = false;
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                    if (e.Args.FirstOrDefault(a => a.StartsWith("--assert-version=")) is { } versionArg)
                    {
                        var actual = typeof(App).Assembly.GetName().Version!.ToString(3);
                        if (actual != versionArg[17..]) throw new InvalidOperationException("Wrong restarted version: " + actual);
                        await File.WriteAllTextAsync(path + ".version.txt", actual);
                    }
                    if (e.Args.Any(a => a.StartsWith("--update-exercise="))) await UpdateSmoke.RunAsync(e.Args, path);
                    if (e.Args.Any(a => a.StartsWith("--browser-persistence="))) await BrowserPersistenceSmoke.RunAsync(window, e.Args);
                    if (e.Args.Contains("--features-exercise")) await FeatureSmoke.RunAsync(window, model, path);
                    if (e.Args.Contains("--usability-exercise")) await UsabilitySmoke.RunAsync(window, model);
                    if (e.Args.Contains("--exit-exercise")) await ExitSmoke.RunAsync(window, path);
                    if (e.Args.Contains("--swagger-exercise")) await SwaggerSmoke.RunAsync(window, path);
                    if (e.Args.Contains("--gateway-exercise"))
                    {
                        await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Response?.StatusCode != 401 || !model.RequestDetails.Contains("curl") || !model.RequestDetails.Contains("/whoami")) throw new InvalidOperationException("Gateway baseline failed: " + model.Message);
                        await File.WriteAllTextAsync(path + ".report.json", model.ReportJson());
                        window.EvidenceTabs.SelectedIndex = 1;
                    }
                    if (e.Args.Contains("--workspace-exercise"))
                    {
                        model.NewIdentity.Execute(null); model.SelectedIdentity!.Secret = "workspace-canary-228";
                        model.EnvironmentName = "Local smoke"; await model.SaveEnvironment.RunAsync();
                        model.SelectedIdentity.Secret = ""; await model.LoadEnvironment.RunAsync();
                        if (!model.Identities.Any(i => i.Secret == "workspace-canary-228")) throw new InvalidOperationException("Protected environment did not restore: " + model.Message);
                        await model.ForgetEnvironment.RunAsync();
                        model.SelectedCase!.BodyKind = Core.RequestBodyKind.Multipart; model.SelectedCase.SendBody = true;
                        model.SelectedCase.Parts.Add(new() { Name = "description", Value = "Upload fixture", Required = true });
                        model.SelectedCase.Parts.Add(new() { Name = "file", FileName = "sample.bin", Binary = true, Value = "AP+A" });
                        model.Database.Draft.Sql = "SELECT 'unsaved query draft'";
                        await model.SaveDraftAsync(); await model.RestoreDraft.RunAsync();
                        if (model.SelectedCase?.Parts.Count != 2 || model.SelectedCase.BodyKind != Core.RequestBodyKind.Multipart || !model.Database.Draft.Sql.Contains("unsaved query draft")) throw new InvalidOperationException("Extended draft recovery failed.");
                        await model.SaveLibrary.RunAsync(); if (model.Library.Count != 1) throw new InvalidOperationException("Library save failed: " + model.Message);
                        var workspaceWindow = new WorkspaceWindow { DataContext = model, Owner = window, ShowInTaskbar = false }; workspaceWindow.Show();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); workspaceWindow.UpdateLayout();
                        var workspaceImage = new RenderTargetBitmap((int)workspaceWindow.ActualWidth, (int)workspaceWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32); workspaceImage.Render(workspaceWindow);
                        var workspaceEncoder = new PngBitmapEncoder(); workspaceEncoder.Frames.Add(BitmapFrame.Create(workspaceImage));
                        using (var stream = File.Create(path + ".workspace.png")) workspaceEncoder.Save(stream);
                        workspaceWindow.Close(); window.RequestTabs.SelectedIndex = 2;
                    }
                    if (e.Args.Contains("--assessment-exercise"))
                    {
                        await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Assessment smoke needs a completed passing attempt.");
                        var editor = model.CreateAssessmentEditor();
                        editor.Reviewer = "Local reviewer"; editor.Verdict = Core.Verdict.Fail; editor.Reason = "HTTP succeeded, but the business requirement needs investigation.";
                        await editor.Record.RunAsync();
                        if (model.SelectedAttempt.ManualAssessments.Count != 1 || model.SelectedAttempt.Verdict != Core.Verdict.Pass || !model.VerdictText.Contains("MANUAL: FAIL")) throw new InvalidOperationException("Manual assessment overwrote or failed to accompany automatic result.");
                        var review = new ManualAssessmentWindow(editor) { Owner = window, ShowInTaskbar = false }; review.Show();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); review.UpdateLayout();
                        var reviewImage = new RenderTargetBitmap((int)review.ActualWidth, (int)review.ActualHeight, 96, 96, PixelFormats.Pbgra32); reviewImage.Render(review);
                        var reviewEncoder = new PngBitmapEncoder(); reviewEncoder.Frames.Add(BitmapFrame.Create(reviewImage));
                        using (var stream = File.Create(path + ".assessment.png")) reviewEncoder.Save(stream);
                        review.Close();
                        await File.WriteAllTextAsync(path + ".report.json", model.ReportJson());
                    }
                    if (e.Args.Contains("--authoring-exercise"))
                    {
                        var authoringWorkspace = Path.Combine(Path.GetTempPath(), "E2ETester-authoring", Guid.NewGuid().ToString("N"));
                        using (var author = new MainViewModel(authoringWorkspace))
                        {
                            author.PackageTitle = "Authored package";
                            author.SelectedCase!.Query[0].Value = "edited-page";
                            author.DuplicateCase.Execute(null);
                            if (author.Cases.Count != 4) throw new InvalidOperationException("Duplicate failed.");
                            author.MoveCaseDown.Execute(null); author.MoveCaseUp.Execute(null); author.DeleteCase.Execute(null);
                            if (author.Cases.Count != 3) throw new InvalidOperationException("Delete failed.");
                            author.SelectedCase = author.Cases[0];
                            await author.SaveDraftAsync();
                        }
                        using (var recovered = new MainViewModel(authoringWorkspace))
                        {
                            await recovered.RestoreDraft.RunAsync();
                            if (recovered.PackageTitle != "Authored package" || recovered.SelectedCase?.Query[0].Value != "edited-page" || recovered.SelectedCase.Query[0].OriginalLabel != "Original: e2e-tester" || !recovered.SelectedCase.Query[0].IsChanged) throw new InvalidOperationException("Draft recovery lost original values or edits: " + recovered.Message);
                            recovered.SelectedCase.Query[0].Restore.Execute(null);
                            if (recovered.SelectedCase.Query[0].Value != "e2e-tester") throw new InvalidOperationException("Recovered original cannot be restored.");
                        }
                        model.PackageTitle = "Authored package"; model.SelectedCase!.Query[0].Value = "edited-page";
                        await model.SaveDraftAsync(); await model.RestoreDraft.RunAsync();
                    }
                    if (e.Args.Contains("--authentication-exercise"))
                    {
                        model.NewIdentity.Execute(null);
                        await model.LoginBrowser.RunAsync();
                        if (model.SelectedIdentity?.BrowserSession is not null || model.SelectedIdentity?.Secret != "" || model.Busy || !model.Message.Contains("HTTPS authority")) throw new InvalidOperationException("Invalid browser configuration was not safely rejected.");
                        window.RequestTabs.SelectedIndex = 3;
                    }
                    if (e.Args.Contains("--history-exercise"))
                    {
                        string? selectedId = null;
                        var history = model.CreateRunHistory(id => selectedId = id);
                        await history.LoadAsync();
                        if (history.Runs.Count != 2 || history.Runs[0].Id != "unfinished-history-demo") throw new InvalidOperationException("History did not load the saved sessions.");
                        var historyWindow = new RunHistoryWindow(history) { Owner = window, ShowInTaskbar = false }; historyWindow.Show();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); historyWindow.UpdateLayout();
                        var historyImage = new RenderTargetBitmap((int)historyWindow.ActualWidth, (int)historyWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32); historyImage.Render(historyWindow);
                        var historyEncoder = new PngBitmapEncoder(); historyEncoder.Frames.Add(BitmapFrame.Create(historyImage));
                        using (var stream = File.Create(path + ".history.png")) historyEncoder.Save(stream);
                        history.Selected = history.Runs[1]; history.Open.Execute(null); historyWindow.Close();
                        await model.RestoreRunAsync(selectedId);
                        if (model.Send.CanExecute(null) || model.Finish.CanExecute(null) || !model.ExportReport.CanExecute(null) || !model.ReportJson().Contains("finished-history-demo")) throw new InvalidOperationException("Finished history must support review, not execution.");
                        await model.RestoreRunAsync("unfinished-history-demo");
                        if (!model.Send.CanExecute(null)) throw new InvalidOperationException("Unfinished history cannot resume.");
                    }
                    if (e.Args.Contains("--exercise"))
                    {
                        await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Desktop health case failed: " + model.Message);
                        model.SelectedCase!.Query[0].Value = "manual-variation";
                        await model.Send.RunAsync();
                        if (model.Attempts.Count != 2 || model.SelectedAttempt?.Changes.Count == 0) throw new InvalidOperationException("Desktop rerun did not retain changes.");
                        await model.AddVariation.RunAsync();
                        await model.Send.RunAsync();
                        if (model.Cases.Count != 4 || model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Desktop variation failed.");
                        model.Next.Execute(null); await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Desktop POST case failed.");
                        model.Next.Execute(null); await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Desktop negative case failed.");
                        await File.WriteAllTextAsync(path + ".report.json", model.ReportJson());
                        await File.WriteAllTextAsync(path + ".package.json", Application.PackageJson.Write(MainViewModel.Demo()));
                        foreach (var tab in Enumerable.Range(0, 6)) { window.RequestTabs.SelectedIndex = tab; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
                        model.SelectedCase = model.Cases[0]; window.RequestTabs.SelectedIndex = 0;
                        await model.SaveDraftAsync();
                        model.SelectedCase.Query[0].Value = "discard-this-unsaved-change";
                        await model.RestoreDraft.RunAsync();
                        if (model.SelectedCase?.Query[0].Value != "manual-variation" || model.Attempts.Count != 2 || model.DuplicateCase.CanExecute(null) || !model.Send.CanExecute(null)) throw new InvalidOperationException("Run-linked draft recovery changed history or authoring restrictions.");
                    }
                    if (e.Args.Contains("--database-exercise"))
                    {
                        await model.LoadDatabaseDemo.RunAsync();
                        model.Database.UseFixture = model.ApplyFixture;
                        await model.Database.RunQuery.RunAsync();
                        if (model.Database.ResultView?.Count != 2) throw new InvalidOperationException("Desktop fixture query failed: " + model.Database.Message);
                        model.Database.SelectedRow = model.Database.ResultView[0]; model.Database.SelectFixture.Execute(null);
                        if (model.SelectedCase?.Fixture is null) throw new InvalidOperationException("Desktop fixture selection failed: " + model.Database.Message);
                        await model.Send.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Inconclusive) throw new InvalidOperationException("Required DB check did not stay pending: " + model.Message);
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        var pendingImage = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); pendingImage.Render(window);
                        var pendingEncoder = new PngBitmapEncoder(); pendingEncoder.Frames.Add(BitmapFrame.Create(pendingImage));
                        using (var stream = File.Create(path + ".inconclusive.png")) pendingEncoder.Save(stream);
                        await model.VerifyDatabase.RunAsync();
                        if (model.SelectedAttempt?.Verdict != Core.Verdict.Pass) throw new InvalidOperationException("Desktop DB verification failed: " + model.Message);
                        await File.WriteAllTextAsync(path + ".report.json", model.ReportJson());
                        await File.WriteAllTextAsync(path + ".package.json", Application.PackageJson.Write(MainViewModel.DatabaseDemoPackage()));
                        var studio = new DatabaseWindow(model.Database) { ShowInTaskbar = false, Owner = window }; studio.Show();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); studio.UpdateLayout();
                        var studioImage = new RenderTargetBitmap((int)studio.ActualWidth, (int)studio.ActualHeight, 96, 96, PixelFormats.Pbgra32); studioImage.Render(studio);
                        var studioEncoder = new PngBitmapEncoder(); studioEncoder.Frames.Add(BitmapFrame.Create(studioImage));
                        using (var stream = File.Create(path + ".studio.png")) studioEncoder.Save(stream);
                        studio.Close(); window.RequestTabs.SelectedIndex = 5;
                    }
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(path)) encoder.Save(stream);
                    listener!.Flush(); listener.Close(); window.Close(); Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); listener?.Flush(); Shutdown(1); }
                };
            }
            window.Show();
        }
        catch (Exception ex)
        {
            if (e.Args.Any(a => a.StartsWith("--smoke="))) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "E2ETester-smoke-error.txt"), ex.ToString()); Shutdown(1); return; }
            var message = ex is Application.PackageFieldValidationException or System.Text.Json.JsonException or InvalidDataException
                ? "A package or saved document could not be loaded because its contents are invalid. No tests were sent."
                : ex is IOException ? "A file or workspace could not be opened. Check file access and whether another instance is using the workspace."
                : "E2E Tester could not start.";
            MessageBox.Show(message + "\n\n" + ex.Message, "E2E Tester", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1);
        }
    }
}
