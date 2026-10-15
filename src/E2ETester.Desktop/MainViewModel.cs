using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Win32;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel : Observable, IDisposable
{
    private readonly SqliteRunStore store;
    private readonly RunCoordinator coordinator;
    private CancellationTokenSource? cancellation;
    private TestPackage package = new();
    private TestRun? run;
    private CaseEditor? selectedCase;
    private Attempt? selectedAttempt;
    private IdentityEditor? selectedIdentity;
    private string baseUrl = "http://localhost:5188", message = "Open a test package or explore the local demo. Nothing runs automatically.";
    private bool busy, runAnyway;
    public bool RunAnyway { get => runAnyway; set => Set(ref runAnyway, value); }
    public ObservableCollection<CaseEditor> Cases { get; } = [];
    public ObservableCollection<Attempt> Attempts { get; } = [];
    public ObservableCollection<IdentityEditor> Identities { get; } = [new()];
    public static string[] Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE"];
    public static AuthKind[] AuthKinds { get; } = Enum.GetValues<AuthKind>();
    public static CheckKind[] CheckKinds { get; } = Enum.GetValues<CheckKind>();
    public string PackageTitle { get => package.Title; set { package.Title = value; Changed(); } }
    public string BaseUrl { get => baseUrl; set { if(Set(ref baseUrl, value)) { RestoreRememberedSessions(); UpdateSwaggerUrlFromPackage(); } } }
    public string Message { get => message; set { message = value; Changed(); } }
    public bool Busy { get => busy; private set { Set(ref busy, value); Changed(nameof(CanEdit)); RefreshCommands(); } }
    public bool CanEdit => !Busy;
    public bool AllCasesCompleted => run is not null && Cases.Count > 0
        && Cases.All(c => run.Attempts.LastOrDefault(a => a.CaseId == c.Baseline.Id)?.State == AttemptState.Completed);
    public string Progress => run is null ? $"{Cases.Count} cases · ready to start" : $"{Cases.Count(c => run.Attempts.Any(a => a.CaseId == c.Baseline.Id))}/{Cases.Count} tested · {run.Attempts.Count} attempts · {(run.FinishedAt is null ? "IN PROGRESS" : Outcomes.ForRun(run).ToString().ToUpperInvariant())}";
    public string RunLabel => run is null ? "NEW SESSION" : $"RUN {run.Id[..8].ToUpperInvariant()}";
    public string VerdictText => SelectedAttempt is null ? "AWAITING REQUEST" : "RESULT: " + Outcomes.ForAttempt(SelectedAttempt).ToString().ToUpperInvariant() + "\nAUTOMATED: " + SelectedAttempt.Verdict.ToString().ToUpperInvariant() + (SelectedAttempt.ManualAssessments.LastOrDefault() is { } assessment ? $"\nMANUAL: {assessment.Verdict.ToString().ToUpperInvariant()}{(ManualAssessments.IsCurrent(SelectedAttempt, assessment) ? "" : " · REVIEW AGAIN")}" : "");
    public string ReportJson() => run is null ? throw new InvalidOperationException("No run has started.") : Reports.Json(run);
    public string ResponseText => SelectedAttempt is not null && SelectedCase?.Baseline.Kind == TestStepKind.SwaggerAuthentication ? "Sign-in step recorded. No API request was sent. Continue to remote-IP validation to check server access." : SelectedAttempt?.Response is { } r ? (r.Error is null ? DisplayResponseBody(r) : $"{r.ErrorCode}\n{r.Error}\n\n{DisplayResponseBody(r)}") : "Send a request to inspect its response here.";
    public string ResponseMeta => SelectedAttempt is not null && SelectedCase?.Baseline.Kind == TestStepKind.SwaggerAuthentication ? "Authentication only · no HTTP request" : SelectedAttempt?.Response is { } r ? $"{r.StatusCode?.ToString() ?? "NO RESPONSE"}  ·  {r.DurationMs} ms  ·  {r.CapturedBytes:N0} bytes · {r.BodyEncoding}{(r.Truncated ? " · TRUNCATED" : "")}" : "No response yet";
    public string ResponseHeaders => SelectedAttempt?.Response is { } r ? string.Join("\n", r.Headers.Select(p => $"{p.Key}: {string.Join(", ", p.Value)}")) : "";
    public string ChangesText => SelectedAttempt is null ? "Every send records its changes from the original case." : string.Join("\n\n", SelectedAttempt.Changes.Select(c => $"{c.Path}\nOriginal: {c.Original}\nSent: {c.Current}"));
    public CaseEditor? SelectedCase
    {
        get => selectedCase;
        set
        {
            if (!Set(ref selectedCase, value)) return;
            RunAnyway = false;
            var alias = value?.IdentityRef ?? "none";
            SelectedIdentity = Identities.FirstOrDefault(i => i.Alias == alias) ?? AddIdentity(alias);
            Changed(nameof(SendLabel)); LoadAttempts(); RefreshCommands();
        }
    }
    public Attempt? SelectedAttempt
    {
        get => selectedAttempt;
        set { Set(ref selectedAttempt, value); Changed(nameof(VerdictText)); Changed(nameof(HasFailureVerdict)); Changed(nameof(ResponseText)); Changed(nameof(RequestDetails)); CopyCurl?.Refresh(); Changed(nameof(ResponseMeta)); Changed(nameof(ResponseHeaders)); Changed(nameof(ChangesText)); Changed(nameof(DatabaseEvidenceText)); VerifyDatabase?.Refresh(); ReviewAttempt?.Refresh(); MarkAttemptFailed?.Refresh(); SaveResponse?.Refresh(); }
    }
    public IdentityEditor? SelectedIdentity
    {
        get => selectedIdentity;
        set
        {
            if (SelectedCase is not null && value is not null)
            {
                // Keep the paired setup request on the identity chosen for sign-in.
                var previous = SelectedCase.IdentityRef;
                if (SelectedCase.Baseline.Kind == TestStepKind.SwaggerAuthentication && Cases.FirstOrDefault() == SelectedCase && Cases.Skip(1).FirstOrDefault() is { } validation && validation.IdentityRef == previous)
                    validation.IdentityRef = value.Alias;
                SelectedCase.IdentityRef = value.Alias;
            }
            Set(ref selectedIdentity, value); LoginBrowser?.Refresh(); ForgetLogin?.Refresh();
        }
    }
    public AsyncCommand Import { get; }
    public AsyncCommand ExportPackage { get; }
    public AsyncCommand Send { get; }
    public AsyncCommand ExportReport { get; }
    public AsyncCommand SaveResponse { get; }
    public AsyncCommand Finish { get; }
    public AsyncCommand Resume { get; }
    public AsyncCommand BrowseRuns { get; }
    public AsyncCommand ReviewAttempt { get; }
    public ActionCommand MarkAttemptFailed { get; }
    public bool HasFailureVerdict => Outcomes.ForAttempt(SelectedAttempt) == Verdict.Fail;
    private static string CaseStatus(Attempt? attempt) => attempt is null ? "NOT RUN" : Outcomes.ForAttempt(attempt).ToString().ToUpperInvariant() + (attempt.ManualAssessments.LastOrDefault() is { } review ? $" · MANUAL: {review.Verdict.ToString().ToUpperInvariant()}{(ManualAssessments.IsCurrent(attempt, review) ? "" : " · REVIEW AGAIN")}" : "");
    public AsyncCommand AddVariation { get; }
    public ActionCommand Cancel { get; }
    public ActionCommand Next { get; }
    public ActionCommand Previous { get; }
    public ActionCommand NewCase { get; }
    public ActionCommand NewRun { get; }
    public ActionCommand AddQuery { get; }
    public ActionCommand AddHeader { get; }
    public ActionCommand AddCheck { get; }
    public ActionCommand NewIdentity { get; }
    public AsyncCommand LoginBrowser { get; }
    public ActionCommand ForgetLogin { get; }

    public MainViewModel(string workspace, string? sessionDirectory = null)
    {
        rememberedSessions = sessionDirectory is null ? null : new RememberedSessions(sessionDirectory);
        useLocalDefaultSuite = sessionDirectory is not null && Path.GetFullPath(sessionDirectory).Equals(Path.GetFullPath(RememberedSessions.DefaultDirectory), StringComparison.OrdinalIgnoreCase);
        workspacePath = System.IO.Path.GetFullPath(workspace);
        store = new SqliteRunStore(workspace); coordinator = new(store, new HttpTransport());
        Import = Command(ImportAsync); ExportPackage = Command(ExportPackageAsync);
        Send = Command(SendAsync, () => SelectedCase is not null && (run?.FinishedAt is null || IsPlayground));
        ExportReport = Command(ExportReportAsync, () => run is not null);
        SaveResponse = Command(SaveResponseAsync, () => SelectedAttempt?.Response is { Truncated: false, ErrorCode: null } response);
        Finish = Command(FinishAsync, () => run is not null && run.FinishedAt is null);
        Resume = Command(ResumeAsync); AddVariation = Command(AddVariationAsync, () => SelectedCase is not null && run?.FinishedAt is null);
        BrowseRuns = Command(BrowseRunsAsync);
        ReviewAttempt = Command(ReviewAttemptAsync, () => SelectedAttempt is { State: not AttemptState.Running });
        MarkAttemptFailed = new(() =>
        {
            var editor = CreateAssessmentEditor(); editor.Verdict = Verdict.Fail;
            new ManualAssessmentWindow(editor) { Owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this) }.ShowDialog();
        }, () => !Busy && run is { FinishedAt: null } && SelectedAttempt is { State: not AttemptState.Running });
        Cancel = new(() => cancellation?.Cancel(), () => Busy);
        Next = new(() => Navigate(1), () => !Busy && SelectedCase is not null && Cases.IndexOf(SelectedCase) < Cases.Count - 1);
        Previous = new(() => Navigate(-1), () => !Busy && SelectedCase is not null && Cases.IndexOf(SelectedCase) > 0);
        NewCase = new(() => { if (IsPlayground) run = null; var c = new CaseEditor(new TestCase { Title = $"Test {Cases.Count + 1}" }); Cases.Add(c); SelectedCase = c; Changed(nameof(Progress)); }, () => !Busy && (run is null || IsPlayground));
        NewRun = new(ResetRun, () => !Busy);
        AddQuery = new(() => SelectedCase?.Query.Add(new(new Field { Name = "parameter" })), () => !Busy && SelectedCase is not null);
        AddHeader = new(() => SelectedCase?.Headers.Add(new(new Field { Name = "X-Header" })), () => !Busy && SelectedCase is not null);
        AddCheck = new(() => SelectedCase?.Checks.Add(new Assertion()), () => !Busy && SelectedCase is not null);
        NewIdentity = new(() => SelectedIdentity = AddIdentity(NextIdentityAlias()), () => !Busy);
        LoginBrowser = Command(LoginBrowserAsync, () => SelectedIdentity?.IsConfigurable == true);
        ForgetLogin = new(() => { SelectedIdentity?.ForgetBrowser(); Message = "Browser session removed from this identity. The browser's own sign-in session is unchanged."; }, () => !Busy && SelectedIdentity?.IsConfigurable == true);
        if (sessionDirectory is not null)
        {
            connectionDefaults = new ConnectionDefaultsStore(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sessionDirectory))!, "Connections"));
            savedConnections = new SavedConnections(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sessionDirectory))!, "Connections"));
            Database.SaveConnectionDefaults = (connection, remove) => {
                savedConnections.Save(BaseUrl, connection.Profile, remove || !connection.RememberLocally);
                connectionDefaults.Save(BaseUrl, DatabaseConnectionDetails.Read(connection.Profile).Details, remove);
            };
        }
        InitializeDatabase();
        InitializeAuthoring();
        InitializeWorkspace(workspace);

        InitializeDiagnostics();
        InitializeTools();
        InitializeGuidedActions();
        InitializePreparation(workspace);
        LoadDatabaseDemo = Command(() => LoadDatabaseDemoAsync(workspace));
        LoadPackage(Demo());
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? allowed = null) => new(action, ex => Message = "Could not complete action: " + ex.Message, () => !Busy && (allowed?.Invoke() ?? true));
    private void RefreshCommands()
    {
        OpenGuidedActions?.Refresh();
        Database.RefreshConnectionActions();
        Changed(nameof(AllCasesCompleted));
        Changed(nameof(PreparationStatus)); Changed(nameof(HasPendingPreparation)); Changed(nameof(HasCasePreparation)); PrepareCase?.Refresh(); RestorePreparation?.Refresh(); ConfigurePreparation?.Refresh();
        foreach (var command in new[] { Import, ExportPackage, Send, ExportReport, SaveResponse, Finish, Resume, AddVariation, BrowseRuns }) command?.Refresh();
        foreach (var command in new[] { Cancel, Next, Previous, NewCase, NewRun, AddQuery, AddHeader, AddCheck, NewIdentity }) command?.Refresh();
        ReviewTesterSetup?.Refresh();
        OpenDatabase?.Refresh(); AddDatabaseCheck?.Refresh(); VerifyDatabase?.Refresh();
        LoadDatabaseDemo?.Refresh();
        LoginBrowser?.Refresh(); ForgetLogin?.Refresh();
        ReviewAttempt?.Refresh(); MarkAttemptFailed?.Refresh();
        CopyCurl?.Refresh(); OpenSwagger?.Refresh(); RemoveField?.Refresh(); LoadCloudSample?.Refresh(); OpenUpdates?.Refresh(); ManageIdentities?.Refresh(); OpenPlayground?.Refresh(); ImportOpenApi?.Refresh(); ViewReport?.Refresh();
        foreach (var command in new[] { OpenWorkspace, SaveEnvironment, LoadEnvironment, ForgetEnvironment, SaveLibrary, LoadLibrary, BackupWorkspace, ExportDiagnostics }) command?.Refresh();
        SaveDraft?.Refresh(); RestoreDraft?.Refresh(); DuplicateCase?.Refresh(); DeleteCase?.Refresh(); MoveCaseUp?.Refresh(); MoveCaseDown?.Refresh(); Changed(nameof(CanAuthor));
    }
    private IdentityEditor AddIdentity(string alias)
    {
        var identity = new IdentityEditor { Alias = alias, Kind = AuthKind.Bearer }; Identities.Add(identity); return identity;
    }
    private void LoadPackage(TestPackage value, bool selectFirst = true)
    {
        draftRecoveryBlocked = false;
        sourcePackageFileName = null;
        package = PackageJson.Clone(value); run = null; Cases.Clear(); Attempts.Clear();
        Database.LoadQueries(package.Queries);
        SeedDatabaseConnections();
        foreach (var c in package.Cases) Cases.Add(new(c));
        if (selectFirst) IncludeReferencedIdentities();
        BaseUrl = package.BaseUrl; SelectedCase = selectFirst ? Cases.FirstOrDefault() : null; SelectedIdentity ??= Identities[0];
        UpdateSwaggerUrlFromPackage();
        RestoreRememberedSessions();
        if (Cases.Any(c => c.FixtureQueryId.Length > 0 || c.DatabaseChecks.Count > 0)) ShowAdvanced = true;
        MarkWorkSaved(); NotifyRun();
    }
    private TestPackage CurrentPackage() => package with { SchemaVersion = package.SchemaVersion == "1.8" || Cases.Select(c => c.Definition()).Concat(package.GuidedActions ?? []).Any(c => c.AppliesWhen is not null || c.Preparation is {} p && IdentityBindings.KeyBinding(p) is not null) ? "1.8" : package.SchemaVersion == "1.7" || Cases.Any(c => c.Definition().Assertions.Any(a => a.Kind == CheckKind.JsonSchema)) ? "1.7" : package.SchemaVersion == "1.6" || package.GuidedActions is not null ? "1.6" : Cases.Any(c => c.Baseline.Preparation is not null) ? "1.5" : "1.4", BaseUrl = BaseUrl, Cases = Cases.Select(c => c.Definition()).ToList(), Queries = PackageJson.Clone(Database.Queries.ToList()) };
    private void NotifyRun() { OpenGuidedActions?.Refresh(); Database.CanSaveQueries = run is null; Changed(nameof(PackageTitle)); Changed(nameof(PackageDescription)); Changed(nameof(Progress)); Changed(nameof(RunLabel)); RefreshCommands(); }
    private void LoadAttempts()
    {
        Attempts.Clear();
        if (run is not null) foreach (var a in run.Attempts.Where(a => a.CaseId == SelectedCase?.Baseline.Id).Reverse()) Attempts.Add(a);
        SelectedAttempt = Attempts.FirstOrDefault();
    }
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "E2E Tester package (*.jsee;*.json)|*.jsee;*.json" };
        if (dialog.ShowDialog() != true) return;
        if (new FileInfo(dialog.FileName).Length > 5_000_000) throw new InvalidDataException("Package exceeds the 5 MB limit.");
        var imported = PackageJson.Import(await File.ReadAllTextAsync(dialog.FileName));
        LoadPackage(IsPlayground ? imported : SuiteDefaults.Ensure(imported)); RememberPackageFile(dialog.FileName); Message = "Package loaded. Review the base URL and authentication before sending.";
    }
    private async Task ExportPackageAsync()
    {
        var value = SuiteDefaults.Ensure(CurrentPackage()); PackageJson.Validate(value);
        var dialog = new SaveFileDialog { Filter = "E2E Tester package (*.jsee)|*.jsee|JSON package (*.json)|*.json", FileName = "test-package.jsee", DefaultExt = ".jsee", AddExtension = true };
        if (dialog.ShowDialog() == true) { await Reports.WriteAtomicAsync(dialog.FileName, PackageJson.Write(PackageJson.Clone(value))); await SaveDraftExplicitlyAsync(); Message = "Package exported with all entered values."; }
    }
    private async Task SendAsync()
    {
        if (SelectedCase is null || SelectedIdentity is null) return;
        if (IsPlayground && run is not null && (run.FinishedAt is not null || run.BaseUrl != BaseUrl)) run = null;
        EnsurePreparationScope();
        var editor = SelectedCase; var identityEditor = SelectedIdentity;
        if (editor.Baseline.Preparation is not null && !PermissionIdentity.IsPermissionPlan(editor.Baseline.Preparation) && !preparations.Any(p => p.Status == "applied" && p.PackageId == package.Id && p.CaseId == editor.Baseline.Id && p.BaseUrl == BaseUrl))
        { Message = "Select Prepare before signing in and sending this case."; return; }
        if (editor.Baseline.Kind == TestStepKind.SwaggerAuthentication && identityEditor.BrowserSession is null && string.IsNullOrWhiteSpace(identityEditor.Secret))
        {
            SwaggerUrl = new Uri(new Uri(BaseUrl.TrimEnd('/') + "/"), editor.Url).AbsoluteUri;
            if (!await ImportSwaggerTokenAsync()) { Message = "No token imported. In Swagger, read the authorized token, then choose Use token for selected identity. No API request was sent."; return; }
            identityEditor = SelectedIdentity!;
        }
        if (editor.Fixture is null && (editor.FixtureRequired || ValueBindings.RequiresFixture(editor.Request(identityEditor.Alias))))
        {
            ShowAdvanced = true;
            Message = "This package requires database data before sending. For /whoami, choose Tools → Cloud test: that API-only package needs no fixtures. Otherwise use Database → Query Studio to select the configured fixture.";
            return;
        }
        var applicability = await IdentityBindings.ApplicabilityAsync(editor.Baseline, ResolveSetupIdentity);
        if(applicability.Count>0 && !RunAnyway) throw new InvalidOperationException("This case may not apply to this tester: " + string.Join(" ",applicability) + " Review tester setup, or select Run anyway to explicitly override. Expected results will not be changed.");
        var bypassPrerequisites = RunAnyway;
        if (bypassPrerequisites && !(RunAnywayConfirmation?.Invoke() ?? (MessageBox.Show(PreparationOwner(), "Run this request despite prerequisites or tester applicability warnings?\n" + string.Join("\n", applicability) + "\nAvailable earlier HTTP responses may supply referenced values even if their test failed. Missing values must still be entered manually. This override is recorded; assertions still run normally.", "Run anyway — this request only", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK))) { RunAnyway = false; return; }
        var request = editor.Request(identityEditor.Alias);
        if (bypassPrerequisites)
        {
            var resolved = ResolveRunAnywayRequest(request);
            if (resolved is null) { RunAnyway = false; return; }
            request = resolved;
        }
        var checks = PackageJson.Clone(editor.Checks.ToList());
        Busy = true; cancellation = new();
        try
        {
            var identity = await identityEditor.ResolveAsync(cancellation.Token);
            await EnsurePreparationReady(identity);
            RememberIdentity(identityEditor);
            identityEditor.RefreshSession();
            if (run is null)
            {
                // Preserve imported baselines; drafts are recorded as explicit per-attempt changes.
                var snapshot = CurrentPackage() with { Cases = Cases.Select(c => c.ExecutionBaseline()).ToList() };
                run = await coordinator.StartAsync(snapshot, BaseUrl);
                foreach (var prepared in preparations.Where(p => p.PackageId == package.Id && p.BaseUrl == BaseUrl)) await AuditPreparation(prepared);
            }
            if (BaseUrl != run.BaseUrl) throw new InvalidOperationException("The run's base URL is frozen. Start a new run to change environments.");
            Message = "Sending once… You can cancel waiting; the server may still process the request.";
            await ArchiveRunPackageAsync();
            var attempt = await coordinator.ExecuteAsync(run, editor.ExecutionBaseline(), request, checks, identity, cancellation.Token, editor.Fixture, preparationConfirmed: true, bypassPrerequisites: bypassPrerequisites, applicabilityConfirmed: true, applicabilityIssues: applicability);
            editor.Status = CaseStatus(attempt);
            LoadAttempts(); Message = $"{editor.Status}. Review the evidence, add a variation, or continue to the next case.";
        }
        finally { RunAnyway = false; identityEditor.RefreshSession(); cancellation.Dispose(); cancellation = null; Busy = false; NotifyRun(); LoadAttempts(); }
    }
    private async Task LoginBrowserAsync()
    {
        if (SelectedIdentity is not { IsConfigurable: true } identity) return;
        Busy = true; cancellation = new();
        // A failed account switch must not silently leave the previous account ready to send.
        identity.ForgetBrowser(); identity.Secret = ""; identity.Kind = AuthKind.Bearer;
        Changed(nameof(SelectedIdentity));
        try
        {
            Message = "Complete sign-in in your browser. Cancel stops waiting; no API request will be sent.";
            var settings = new BrowserLoginSettings(identity.Authority, identity.ClientId, identity.Scope, identity.RedirectUri, identity.ExpectedSubject, identity.AllowLocalHttp);
            identity.InstallBrowser(await BrowserTokenSession.LoginAsync(settings, cancellationToken: cancellation.Token));
            await RememberBrowserIdentityAsync(identity);
            Changed(nameof(SelectedIdentity));
            Message = "Signed in. Review the verified subject and issuer in Authentication before sending. Use another identity profile for a different user.";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
    private async Task AddVariationAsync()
    {
        if (SelectedCase is null) return;
        var definition = SelectedCase.Definition(SelectedIdentity?.Alias);
        definition.Id = Guid.NewGuid().ToString("N"); definition.ParentId = SelectedCase.Baseline.Id; definition.Title += " · variation";
        var index = Cases.IndexOf(SelectedCase) + 1;
        if (run is not null)
        {
            var safe = PackageJson.Clone(new TestPackage { Cases = [definition] }).Cases[0];
            run.Package.Cases.Insert(index, safe);
            try { await coordinator.SaveAsync(run); } catch { run.Package.Cases.RemoveAt(index); throw; }
        }
        var added = new CaseEditor(definition);
        if (SelectedCase.Fixture is { } fixture) added.ApplyFixture(fixture);
        Cases.Insert(index, added); SelectedCase = added;
        Message = "Variation inserted next. Review its expected results before sending."; NotifyRun();
    }
    private async Task ResumeAsync()
    {
        await RestoreRunAsync();
    }
    public async Task RestoreRunAsync(string? id = null)
    {
        Busy = true;
        try
        {
        var saved = await coordinator.ResumeAsync(id);
        if (saved is null) { Message = "No saved run is available yet."; return; }
        LoadPackage(saved.Package); run = saved; BaseUrl = saved.BaseUrl;
        foreach (var c in Cases) c.Status = CaseStatus(run.Attempts.LastOrDefault(a => a.CaseId == c.Baseline.Id));
        LoadAttempts(); NotifyRun(); Message = saved.FinishedAt is null ? "Run restored with saved values. Requests were not replayed." : "Finished run opened for review and report export. Start a new run to send more requests.";
        }
        finally { Busy = false; }
    }
    private async Task BrowseRunsAsync()
    {
        string? id = null;
        RunHistoryWindow? window = null;
        var history = CreateRunHistory(selected => { id = selected; window!.Close(); });
        Busy = true;
        try { await history.LoadAsync(); }
        finally { Busy = false; }
        window = new RunHistoryWindow(history) { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
        if (id is not null) await RestoreRunAsync(id);
    }
    internal RunHistoryViewModel CreateRunHistory(Action<string> open) => new(store, open);
    internal AssessmentEditor CreateAssessmentEditor()
    {
        var attempt = SelectedAttempt ?? throw new InvalidOperationException("Select an attempt first.");
        var recordedRun = run ?? throw new InvalidOperationException("No run is open.");
        return new(attempt, run.FinishedAt is null, async (reviewer, verdict, reason) =>
        {
            Busy = true;
            try { await coordinator.AssessAsync(recordedRun, attempt, reviewer, verdict, reason, Identities.Select(i => i.Secret).Concat(Database.Connections.Select(c => c.Profile.ConnectionString))); }
            finally { Busy = false; }
            RefreshDatabaseEvidence();
            Message = $"Manual {verdict.ToString().ToUpperInvariant()} recorded. The automated result and review reason are retained in the report.";
        });
    }
    private Task ReviewAttemptAsync()
    {
        new ManualAssessmentWindow(CreateAssessmentEditor()) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        return Task.CompletedTask;
    }
    private async Task ExportReportAsync()
    {
        if (run is null) return;
        var files = await SaveLocalReportsAsync();
        Message = "Reports saved locally: " + files.Simple + " and " + Path.GetFileName(files.Full);
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Optional: copy both reports to another folder" };
        if (dialog.ShowDialog() == true)
        {
            foreach (var path in new[] { files.Simple, files.Full })
            {
                var destination = Path.Combine(dialog.FolderName, Path.GetFileName(path));
                if (!Path.GetFullPath(destination).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) File.Copy(path, destination, false);
            }
            Message = "Simple report and full evidence ZIP copied to " + dialog.FolderName;
        }
        ShowReport();
    }

    private async Task SaveResponseAsync()
    {
        if (SelectedAttempt?.Response is not { Truncated: false, ErrorCode: null } response) return;
        var dialog = new SaveFileDialog { Filter = "Response content|*.*", FileName = response.BodyEncoding == "base64" ? "response.bin" : "response.txt" };
        if (dialog.ShowDialog() != true) return;
        var bytes = response.BodyEncoding == "base64" ? Convert.FromBase64String(response.Body) : System.Text.Encoding.UTF8.GetBytes(response.Body);
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, dialog.FileName, true); Message = "Captured response saved. Text is exported as UTF-8; binary bytes retain their original encoding."; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task FinishAsync()
    {
        if (run is null) return;
        if (MessageBox.Show("Finish this run and export its report?\n\nAny untested cases remain explicitly incomplete. Choose No to add another test or continue testing.", "Finish test run", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RestorePreparationsAsync();
        run.FinishedAt = DateTimeOffset.UtcNow;
        try { await coordinator.SaveAsync(run); } catch { run.FinishedAt = null; throw; }
        NotifyRun(); await ExportReportAsync();
    }
    private void ResetRun()
    {
        if (run is not null && MessageBox.Show("Start a new run with the current drafts? The previous run remains saved.", "New run", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        LoadPackage(CurrentPackage()); Message = "New run ready. Current draft values become its baseline.";
    }
    private void Navigate(int direction) { if (SelectedCase is not null) SelectedCase = Cases[Cases.IndexOf(SelectedCase) + direction]; }
    public void Dispose() { cancellation?.Cancel(); store.Dispose(); }
    public static TestPackage Demo() => new()
    {
        Id = "local-demo", Title = "API essentials", Description = "Run the bundled local demo server first. No external service is contacted.",
        Cases =
        [
            new() { Id = "health", Title = "01 · Check service health", Instructions = "Confirm the service is healthy. Try changing the query parameter, then add a variation.", Request = new() { Url = "/health", Query = [new() { Name = "source", Value = "e2e-tester", Required = true }], Headers = [new() { Name = "Accept", Value = "application/json", Required = true }] }, Assertions = [new(), new() { Kind = CheckKind.JsonEquals, Selector = "/status", Expected = "\"ok\"" }] },
            new() { Id = "echo", Title = "02 · Submit a payload", Instructions = "Send the fixture payload and verify the echoed name. Changes remain visible in attempt history.", Request = new() { Method = "POST", Url = "/echo", SendBody = true, Body = "{\n  \"name\": \"Example test\",\n  \"enabled\": true\n}" }, Assertions = [new(), new() { Kind = CheckKind.JsonEquals, Selector = "/name", Expected = "\"Example test\"" }] },
            new() { Id = "denied", Title = "03 · Verify access rejection", Instructions = "This endpoint intentionally rejects the request. The expected 403 is a passing test.", Request = new() { Url = "/denied" }, Assertions = [new() { Expected = "403" }] }
        ]
    };
}
