using System.IO;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;
namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    public string WindowTitle => AppInfo.Name + " " + AppInfo.VersionLabel + (IsPlayground ? " · API workspace" : " · Guided API testing");
    public ActionCommand ManageIdentities { get; private set; } = null!;
    public ActionCommand ShowAbout { get; private set; } = null!;
    public bool IsPlayground { get; private set; }
    public string SendLabel => SelectedCase?.Baseline.Kind == TestStepKind.SwaggerAuthentication ? "Complete sign-in step" : "Send request";
    public ActionCommand OpenPlayground { get; private set; } = null!;
    public ActionCommand ImportOpenApi { get; private set; } = null!;
    public ActionCommand ViewReport { get; private set; } = null!;
    public ActionCommand AgentHelp { get; private set; } = null!;
    private void InitializeTools()
    {
        ManageIdentities = new(() => { PruneExpiredIdentities(); new IdentityManagerWindow(this) { Owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this) }.ShowDialog(); }, () => !Busy && !Database.Busy);
        ShowAbout = new(() => new AboutWindow { Owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this) }.ShowDialog());
        OpenPlayground = new(() =>
        {
            var location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2ETester", "Workspaces", "playground");
            try
            {
                var model = new MainViewModel(location, RememberedSessions.DefaultDirectory); model.IsPlayground = true;
                model.LoadPackage(new TestPackage { Title="API workspace", BaseUrl=BaseUrl, Cases=[new() { Title="Untitled request", Instructions="Explore freely. Requests and drafts are saved separately from test suites. Use Import OpenAPI to start from a specification." }] });
                new MainWindow(model).Show();
            } catch(IOException) { Message="The API workspace is already open. Use its window to continue."; }
        }, () => !Busy);
        ImportOpenApi = new(() =>
        {
            if (!IsPlayground) { Message="Open API workspace to import and explore requests separately from this suite."; OpenPlayground.Execute(null); return; }
            var picker = new OpenApiWindow(BaseUrl) { Owner=System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w=>w.DataContext==this) };
            var accepted=picker.ShowDialog()==true;
            if(accepted && picker.ImportedPackage is {} imported) { LoadPackage(imported); Message="Imported package. Review warnings and add missing assertions before running."; return; }
            if (accepted && picker.Selected is { } op)
            {
                run = null;
                op.Case.Request.Url = RequestUrls.Resolve(op.BaseUrl, op.Case.Request).AbsoluteUri;
                var added = new CaseEditor(op.Case); Cases.Add(added); SelectedCase=added; NotifyRun();
                Message="OpenAPI request imported. Review the server address and every value before sending.";
            }
        }, () => !Busy);
        ViewReport = new(ShowReport, () => !Busy && run is not null);
        AgentHelp = new(() => AgentResources.ShowHelp(System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w=>w.DataContext==this)));
    }
    private void ShowReport()
    {
        if(run is null)return;
        new TextDocumentWindow("Test report", ("Readable report", Reports.Text(run)), ("Structured JSON", Reports.Json(run))) { Owner=System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w=>w.DataContext==this) }.ShowDialog();
    }
    private bool IsSetupCase(CaseEditor item) => !IsPlayground && Cases.FirstOrDefault()?.Baseline.Kind == TestStepKind.SwaggerAuthentication && Cases.IndexOf(item) < 2;
    public void LoadNewSuite()
    {
        var localSuite=Path.Combine(RememberedSessions.Root,"Packages","default-suite.jsee");
        if(useLocalDefaultSuite && File.Exists(localSuite)) OpenPackageFile(localSuite);
        else LoadPackage(SuiteDefaults.Create());

    }
    public void OpenPackageFile(string path)
    {
        if(new FileInfo(path).Length>5_000_000)throw new InvalidDataException("Package exceeds 5 MB.");
        LoadPackage(SuiteDefaults.Ensure(PackageJson.Import(File.ReadAllText(path))));
        Message="Package opened. Nothing has been sent. Start with Swagger sign-in.";
    }
}
