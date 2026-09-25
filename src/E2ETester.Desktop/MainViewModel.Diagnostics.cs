using System.Windows;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    private readonly string workspacePath;
    private bool showAdvanced;
    public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
    public ParameterCommand<FieldEditor> RemoveField { get; private set; } = null!;
    public AsyncCommand LoadCloudSample { get; private set; } = null!;
    public ActionCommand OpenUpdates { get; private set; } = null!;
    private string updateLabel="Updates…";
    public string UpdateLabel {get=>updateLabel;private set=>Set(ref updateLabel,value);}
    internal async Task CheckForUpdatesOnStartupAsync(Window owner)
    {
        var check=new UpdatesViewModel(()=>Task.CompletedTask);
        if(await check.CheckAtStartupAsync())
        {
            UpdateLabel=$"Update {check.NewestVersion} available";
            if(owner.IsVisible && !Busy && !Database.Busy && owner.OwnedWindows.Count==0 && MessageBox.Show(owner,$"E2E Tester {check.NewestVersion} is available. Open Updates to download and install it?", "Update available", MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)
                OpenUpdates.Execute(null);
        }
    }
    public string ThemeLabel => ThemeManager.IsDark ? "☀ Light" : "☾ Dark";
    public string RequestDetails => SelectedAttempt is not null && SelectedCase?.Baseline.Kind == TestStepKind.SwaggerAuthentication ? "Authentication-only step: no HTTP request or cURL was generated." : SelectedAttempt is null ? "Send a request to inspect its resolved URL, headers, body and cURL here." : RequestDiagnostics.Describe(SelectedAttempt);
    private string swaggerUrl = "https://api.example.test/swagger/";
    public string SwaggerUrl { get => swaggerUrl; set => Set(ref swaggerUrl, value); }
    private void UpdateSwaggerUrlFromPackage()
    {
        var address = Cases.FirstOrDefault(c => c.Baseline.Kind == TestStepKind.SwaggerAuthentication)?.Url ?? "/swagger/";
        // Base URL can be temporarily incomplete while the user types. Never keep
        // another package's sign-in address when the new address cannot resolve.
        SwaggerUrl = Uri.TryCreate(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)
            && Uri.TryCreate(origin, address, out var resolved)
            && resolved.Scheme is "https" or "http" ? resolved.AbsoluteUri : "";
    }
    public ActionCommand ToggleTheme { get; private set; } = null!;
    public ActionCommand CopyCurl { get; private set; } = null!;
    public AsyncCommand OpenSwagger { get; private set; } = null!;
    private void InitializeDiagnostics()
    {
        OpenUpdates = new(() => new UpdatesWindow(new UpdatesViewModel(PrepareForUpdateAsync, workspace: workspacePath)) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog(), () => !Busy);
        RemoveField = new(field => { SelectedCase?.Query.Remove(field); SelectedCase?.Headers.Remove(field); Message = "Field removed from the draft. Previous attempts are unchanged."; }, _ => !Busy && SelectedCase is not null);
        LoadCloudSample = Command(async () => { await SaveDraftAsync(); LoadGatewayAuthCheck(); await SaveDraftAsync(); });
        ToggleTheme = new(() => { ThemeManager.Apply(!ThemeManager.IsDark); Changed(nameof(ThemeLabel)); });
        CopyCurl = new(() =>
        {
            if (SelectedAttempt?.Response?.Requests.FirstOrDefault() is not { } request) return;
            Clipboard.SetText(RequestDiagnostics.Curl(request, SelectedAttempt.Request.TimeoutSeconds));
            Message = "cURL copied with captured values (Bash syntax).";
        }, () => SelectedAttempt?.Response?.Requests.Count > 0);
        OpenSwagger = Command(async () =>
        {
            if (await ImportSwaggerTokenAsync() && SelectedCase?.Baseline.Kind == TestStepKind.SwaggerAuthentication)
                await SendAsync();
        });
    }
    internal async Task PrepareForUpdateAsync()
    {
        if (Busy || Database.Busy) throw new InvalidOperationException("Finish or cancel the current operation before changing version.");
        Busy = true;
        try
        {
            await RestorePreparationsAsync();
            if (HasPendingPreparation) throw new InvalidOperationException("Restore all prepared changes before changing version.");
            await SaveDraftExplicitlyAsync();
            var backups = System.IO.Path.Combine(workspacePath, "Backups");
            System.IO.Directory.CreateDirectory(backups);
            await store.BackupAsync(System.IO.Path.Combine(backups, "before-update-" + Guid.NewGuid().ToString("N") + ".db"));
        }
        finally { Busy = false; }
    }
    private async Task<bool> ImportSwaggerTokenAsync()
    {
            _ = SwaggerTokenCapture.ValidateAddress(SwaggerUrl);
            if (SelectedIdentity is not { IsConfigurable: true }) NewIdentity.Execute(null);
            var identity = SelectedIdentity!;
            await PreserveBrowserIdentityAsync(identity);
            var imported = false;
            new SwaggerLoginWindow(SwaggerUrl, token =>
            {
                identity.Kind = AuthKind.Bearer; identity.Secret = token; Changed(nameof(SelectedIdentity));
                imported = true;
                Message = $"Swagger token ready for '{identity.Alias}'. This supplied token does not refresh automatically.";
                try { RememberIdentity(identity); }
                catch { Message = "Token imported for this session, but could not be saved in AppData. Check disk space and access permissions."; }
            }, identity.BrowserProfileKey) { Owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this) }.ShowDialog();
            return imported;
    }
    public void LoadGatewayAuthCheck()
    {
        var localSuite=System.IO.Path.Combine(RememberedSessions.Root,"Packages","default-suite.jsee");
        if(useLocalDefaultSuite && System.IO.File.Exists(localSuite)) { OpenPackageFile(localSuite); ShowAdvanced=false; return; }
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Samples/cloud-checkremoteip.json"));
        using var reader = new System.IO.StreamReader(resource.Stream);
        LoadPackage(SuiteDefaults.Create("Example Cloud · Remote IP round trip")); ShowAdvanced = false;
        Message = "Start with Swagger sign-in, then validate the remote IP. Nothing has been sent.";
    }
}
