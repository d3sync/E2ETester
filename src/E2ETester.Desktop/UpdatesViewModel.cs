using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace E2ETester.Desktop;
public sealed class UpdatesViewModel : Observable
{
    private readonly Func<Task> prepare;
    private readonly string? workspace;
    private string source = "", status = "Set a release feed, save settings, then check for updates. Updates are downloaded and installed only when you choose.";
    private UpdateManager? manager;
    private VelopackAsset? selected;
    private bool busy;
    private CancellationTokenSource? cancellation;
    private readonly string settingsDirectory;
    private bool checkOnStartup=true;
    public bool CheckOnStartup {get=>checkOnStartup;set=>Set(ref checkOnStartup,value);}
    public bool HasNewerVersion => Versions.Any(a=>a.Version>Velopack.SemanticVersion.Parse(CurrentVersion));
    public string? NewestVersion => HasNewerVersion ? Versions.First().Version.ToString() : null;
    public string Source { get => source; set { if (Set(ref source, value)) { Versions.Clear(); Selected = null; manager = null; Refresh(); } } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string CurrentVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public bool Busy { get => busy; private set { Set(ref busy, value); Changed(nameof(CanEdit)); Refresh(); } }
    public bool CanEdit => !Busy;
    public ObservableCollection<VelopackAsset> Versions { get; } = [];
    public VelopackAsset? Selected { get => selected; set { Set(ref selected, value); Changed(nameof(InstallLabel)); Refresh(); } }
    public string InstallLabel => manager?.CurrentVersion is { } current && Selected?.Version < current ? "Roll back to selected version" : "Install selected version";
    public AsyncCommand RefreshVersions { get; }
    public AsyncCommand Install { get; }
    public ActionCommand Cancel { get; }
    public ActionCommand SaveSettings {get;}
    public UpdatesViewModel(Func<Task> prepare, string? settingsDirectory=null, string? workspace=null)
    {
        this.prepare = prepare;
        this.workspace = workspace;
        this.settingsDirectory=settingsDirectory??UpdatePreferences.DirectoryPath;
        try {var saved=UpdatePreferences.Load(this.settingsDirectory);source=saved.Source;checkOnStartup=saved.CheckOnStartup;} catch {status="Update settings could not be read. Enter and save your feed again.";}
        SaveSettings=new(()=>
        {
            try {SavePreferences();Status="Update settings saved. Startup checks never download or install automatically.";}
            catch {Status="Could not save settings. Use an HTTPS feed directory or an absolute local folder.";}
        },()=>!Busy);
        RefreshVersions = new(LoadAsync, _ => Status = "Could not read this release feed. It must serve releases.win.json and its package files. Check the address and network connection.", () => !Busy);
        Install = new(InstallAsync, _ => Status = "Version installation did not complete. Your current application and workspace remain available.", () => !Busy && Selected is not null && manager?.IsInstalled == true && Selected.Version != manager.CurrentVersion);
        Cancel = new(() => cancellation?.Cancel(), () => Busy && cancellation is not null);
    }
    private void Refresh() { RefreshVersions?.Refresh(); Install?.Refresh(); Cancel?.Refresh(); SaveSettings?.Refresh(); }
    private void SavePreferences()
    {
        var input=Source.Trim();if(input.Length>0)_=CreateSource(input);
        new UpdatePreferences(input,CheckOnStartup).Save(settingsDirectory);
    }
    internal async Task<bool> CheckAtStartupAsync()
    {
        if(!CheckOnStartup || string.IsNullOrWhiteSpace(Source))return false;
        try {await LoadAsync();return HasNewerVersion;}
        catch {Status="Could not check for updates. Retry from Tools → Updates and settings.";return false;}
    }
    internal static IUpdateSource CreateSource(string value)
    {
        if (Path.IsPathFullyQualified(value) && !value.Contains("://")) return new SimpleFileSource(new DirectoryInfo(value));
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Use an HTTPS release-feed directory without credentials, or an absolute local folder.");
        return new SimpleWebSource(uri.AbsoluteUri);
    }
    private async Task LoadAsync()
    {
        Busy = true; Versions.Clear(); Selected = null;
        try
        {
            var candidates=UpdatePreferences.SourceCandidates(Source);
            IUpdateSource? updateSource=null; UpdateManager? nextManager=null; List<VelopackAsset>? assets=null; string? usedCandidate=null; Exception? failure=null;
            foreach(var candidate in candidates)
            {
                try
                {
                    var attemptSource=CreateSource(candidate);
                    var attemptFeed=await attemptSource.GetReleaseFeed(NullVelopackLogger.Instance, "E2ETester", "win").WaitAsync(TimeSpan.FromSeconds(30));
                    updateSource=attemptSource; nextManager=new UpdateManager(attemptSource); assets=attemptFeed.Assets.ToList(); usedCandidate=candidate;
                    break;
                }
                catch(Exception ex) { failure=ex; }
            }
            if(updateSource is null || nextManager is null || assets is null || usedCandidate is null) throw failure ?? new InvalidDataException("No update feed source was available.");
            manager = nextManager;
            foreach (var asset in assets.Where(a => a.Type == VelopackAssetType.Full && a.PackageId == "E2ETester").OrderByDescending(a => a.Version)) Versions.Add(asset);
            Selected = Versions.FirstOrDefault();
            SavePreferences();
            var summary=Versions.Count==0 ? "The feed contains no compatible versions." : HasNewerVersion ? $"Version {NewestVersion} is available." : "You're up to date.";
            if(!string.Equals(usedCandidate,Source.Trim(),StringComparison.OrdinalIgnoreCase)) summary="Primary feed unavailable; using the d3sync mirror. " + summary;
            Status = summary + (manager.IsInstalled ? " Select a version to install or roll back. A workspace backup is created before applying." : " Use the Velopack installer to enable installation and rollback; the standalone executable cannot replace itself.");
        }
        finally { Busy = false; }
    }
    internal static string[] RestartArguments(string workspace, bool dark) => dark ? ["--workspace=" + Path.GetFullPath(workspace), "--dark"] : ["--workspace=" + Path.GetFullPath(workspace)];
    private async Task InstallAsync()
    {
        var target = Selected; var updater = manager;
        if (target is null || updater?.IsInstalled != true) return;
        if (MessageBox.Show($"Install version {target.Version} and restart?\n\nThe current draft and a workspace backup will be saved first. Older versions may not understand newer test features. Browser data stays outside the installation.", "Change application version", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Busy = true; cancellation = new(); Refresh();
        try
        {
            var update = new UpdateInfo(target, updater.CurrentVersion is { } current && target.Version < current);
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            await updater.DownloadUpdatesAsync(update, percent => dispatcher.BeginInvoke(new Action(() => Status = $"Downloading {target.Version}: {percent}%")), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await prepare();
            // No --auth-check on restart: recover the saved workspace, do not replace it with a sample.
            var restart = RestartArguments(workspace ?? throw new InvalidOperationException("Current workspace is unavailable."), ThemeManager.IsDark);
            updater.ApplyUpdatesAndRestart(target, restart);
        }
        catch (OperationCanceledException) { Status = "Download cancelled. No application version was changed."; }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
}
