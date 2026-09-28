using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Win32;

namespace E2ETester.Desktop;
public sealed record SavedIdentity(string Alias, AuthKind Kind, string Secret, string HeaderName, string Prefix, string Authority, string ClientId, string Scope, string RedirectUri, string ExpectedSubject, bool AutomaticRefresh, bool AllowLocalHttp);
public sealed record SavedEnvironment(string Name, string BaseUrl, List<SavedIdentity> Identities, List<DatabaseConnectionProfile> Databases);
public sealed partial class MainViewModel
{
    private ProtectedProfileStore profiles = null!;
    public ObservableCollection<LibraryEntry> Library { get; } = [];
    public ObservableCollection<string> Environments { get; } = [];
    public LibraryEntry? SelectedLibrary { get; set; }
    private string environmentName = "Local", diagnostics = "";
    public string EnvironmentName { get => environmentName; set => Set(ref environmentName, value); }
    public string Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }
    public AsyncCommand OpenWorkspace { get; private set; } = null!;
    public AsyncCommand SaveEnvironment { get; private set; } = null!;
    public AsyncCommand LoadEnvironment { get; private set; } = null!;
    public AsyncCommand ForgetEnvironment { get; private set; } = null!;
    public AsyncCommand SaveLibrary { get; private set; } = null!;
    public AsyncCommand LoadLibrary { get; private set; } = null!;
    public AsyncCommand BackupWorkspace { get; private set; } = null!;
    public AsyncCommand ExportDiagnostics { get; private set; } = null!;

    private void InitializeWorkspace(string directory)
    {
        profiles = new(directory);
        OpenWorkspace = Command(async () =>
        {
            await RefreshWorkspaceAsync();
            new WorkspaceWindow { DataContext = this, Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        });
        SaveEnvironment = Command(async () =>
        {
            if (string.IsNullOrWhiteSpace(EnvironmentName)) throw new InvalidOperationException("Name this environment first.");
            _ = RequestUrls.Resolve(BaseUrl, new RequestDefinition { Url = BaseUrl });
            var saved = await profiles.LoadAsync<List<SavedEnvironment>>() ?? [];
            saved.RemoveAll(e => e.Name == EnvironmentName);
            saved.Add(new(EnvironmentName, BaseUrl, Identities.Where(i => i.IsConfigurable).Select(i => new SavedIdentity(i.Alias, i.Kind, i.Secret, i.HeaderName, i.Prefix, i.Authority, i.ClientId, i.Scope, i.RedirectUri, i.ExpectedSubject, i.AutomaticRefresh, i.AllowLocalHttp)).ToList(), Database.Connections.Select(c => c.Profile).ToList()));
            await profiles.SaveAsync(saved); await RefreshWorkspaceAsync(); Message = "Environment saved with Windows user encryption. Saved browser sessions and valid tokens remain available across restarts.";
        });
        LoadEnvironment = Command(async () =>
        {
            var saved = (await profiles.LoadAsync<List<SavedEnvironment>>())?.SingleOrDefault(e => e.Name == EnvironmentName) ?? throw new InvalidOperationException("Choose a saved environment.");
            if (run is not null) throw new InvalidOperationException("Start a new run before switching environments.");
            BaseUrl = saved.BaseUrl; Identities.Clear(); Identities.Add(new());
            foreach (var i in saved.Identities) Identities.Add(new() { Alias = i.Alias, Kind = i.Kind, Secret = i.Secret, HeaderName = i.HeaderName, Prefix = i.Prefix, Authority = i.Authority, ClientId = i.ClientId, Scope = i.Scope, RedirectUri = i.RedirectUri, ExpectedSubject = i.ExpectedSubject, AutomaticRefresh = i.AutomaticRefresh, AllowLocalHttp = i.AllowLocalHttp });
            Database.Connections.Clear(); foreach (var d in saved.Databases) Database.Connections.Add(new() { Alias = d.Alias, Provider = d.Provider, ConnectionString = d.ConnectionString, ReadOnlyCredentialsConfirmed = d.ReadOnlyCredentialsConfirmed });
            SelectedIdentity = Identities.FirstOrDefault(i => i.Alias == SelectedCase?.IdentityRef) ?? Identities[0];
            Database.SelectedConnection = Database.Connections.FirstOrDefault(c => c.Alias == Database.Draft.ConnectionRef);
            Message = "Environment restored locally. Valid remembered identities can be reused.";
        });
        ForgetEnvironment = Command(async () =>
        {
            var saved = await profiles.LoadAsync<List<SavedEnvironment>>() ?? [];
            saved.RemoveAll(e => e.Name == EnvironmentName); await profiles.SaveAsync(saved); await RefreshWorkspaceAsync();
            Message = "Saved environment removed. In-memory tokens remain until forgotten or the app closes.";
        });
        SaveLibrary = Command(async () => { await store.SavePackageAsync(SuiteDefaults.Ensure(CurrentPackage()), Identities.Select(i => i.Secret)); await SaveDraftExplicitlyAsync(); await RefreshWorkspaceAsync(); Message = "A package snapshot with all entered values was added to your library."; });
        LoadLibrary = Command(async () => { if (SelectedLibrary is null) return; await SaveDraftAsync(); var p = await store.LoadPackageAsync(SelectedLibrary.Id); if (p is not null) LoadPackage(IsPlayground ? p : SuiteDefaults.Ensure(p)); Message = "Library package opened as a new session. Nothing has run."; });
        BackupWorkspace = Command(async () =>
        {
            var dialog = new SaveFileDialog { Filter = "Workspace backup|*.db", FileName = "e2e-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
            if (dialog.ShowDialog() != true) return;
            await SaveDraftAsync(); await store.BackupAsync(dialog.FileName); Message = "Consistent journal backup created. It includes business evidence but excludes encrypted profiles. See workspace documentation to restore.";
        });
        ExportDiagnostics = Command(async () =>
        {
            var dialog = new SaveFileDialog { Filter = "Safe diagnostics|*.json", FileName = "diagnostics.json" };
            if (dialog.ShowDialog() == true) { await Reports.WriteAtomicAsync(dialog.FileName, await store.DiagnosticsAsync()); Message = "Diagnostics exported without requests, paths or credentials."; }
        });
    }
    private async Task RefreshWorkspaceAsync()
    {
        Library.Clear(); foreach (var entry in await store.ListPackagesAsync()) Library.Add(entry);
        Environments.Clear(); foreach (var profile in await profiles.LoadAsync<List<SavedEnvironment>>() ?? []) Environments.Add(profile.Name);
        Diagnostics = await store.DiagnosticsAsync();
    }
}
