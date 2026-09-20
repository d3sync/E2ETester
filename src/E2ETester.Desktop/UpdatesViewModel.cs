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
    private string source = "", status = "Choose your project's HTTPS release feed or a local release folder. No background updates are performed.";
    private UpdateManager? manager;
    private VelopackAsset? selected;
    private bool busy;
    private CancellationTokenSource? cancellation;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2ETester", "update-source.txt");
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
    public UpdatesViewModel(Func<Task> prepare)
    {
        this.prepare = prepare;
        try { if (File.Exists(SettingsPath)) source = File.ReadAllText(SettingsPath); } catch (IOException) { }
        RefreshVersions = new(LoadAsync, _ => Status = "Could not read this release feed. Check the address and network connection.", () => !Busy);
        Install = new(InstallAsync, _ => Status = "Version installation did not complete. Your current application and workspace remain available.", () => !Busy && Selected is not null && manager?.IsInstalled == true && Selected.Version != manager.CurrentVersion);
        Cancel = new(() => cancellation?.Cancel(), () => Busy && cancellation is not null);
    }
    private void Refresh() { RefreshVersions?.Refresh(); Install?.Refresh(); Cancel?.Refresh(); }
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
            var input = Source.Trim(); var updateSource = CreateSource(input);
            var nextManager = new UpdateManager(updateSource);
            var feed = await updateSource.GetReleaseFeed(NullVelopackLogger.Instance, "E2ETester", "win").WaitAsync(TimeSpan.FromSeconds(30));
            manager = nextManager;
            foreach (var asset in feed.Assets.Where(a => a.Type == VelopackAssetType.Full && a.PackageId == "E2ETester").OrderByDescending(a => a.Version)) Versions.Add(asset);
            Selected = Versions.FirstOrDefault();
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); await File.WriteAllTextAsync(SettingsPath, input);
            Status = manager.IsInstalled ? $"{Versions.Count} available versions. Select an earlier version to roll back. A workspace backup is created before applying." : "Versions loaded. Use the Velopack installer to enable installation and rollback; the standalone executable cannot replace itself.";
        }
        finally { Busy = false; }
    }
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
            var restart = Environment.GetCommandLineArgs().Skip(1).Where(a => a.StartsWith("--workspace=") || a == "--dark").ToArray();
            updater.ApplyUpdatesAndRestart(target, restart);
        }
        catch (OperationCanceledException) { Status = "Download cancelled. No application version was changed."; }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
}
