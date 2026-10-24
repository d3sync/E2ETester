using System.ComponentModel;

using System.Runtime.CompilerServices;

using System.Windows.Input;

using E2ETester.Application;

using E2ETester.Core;



namespace E2ETester.Desktop;



public abstract class Observable : INotifyPropertyChanged

{

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)

    {

        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value; Changed(property); return true;

    }

}



public sealed class ActionCommand(Action action, Func<bool>? enabled = null) : ICommand

{

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;

    public void Execute(object? parameter) => action();

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

}



public sealed class ParameterCommand<T>(Action<T> action, Func<T, bool>? enabled = null) : ICommand where T : class

{

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => parameter is T value && (enabled?.Invoke(value) ?? true);

    public void Execute(object? parameter) { if (parameter is T value && CanExecute(value)) action(value); }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

}



public sealed class AsyncCommand(Func<Task> action, Action<Exception> onError, Func<bool>? enabled = null) : ICommand

{

    private bool running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running && (enabled?.Invoke() ?? true);

    public async void Execute(object? parameter) => await RunAsync();

    public async Task RunAsync()

    {

        if (!CanExecute(null)) return;

        running = true; Refresh();

        try { await action(); }

        catch (Exception ex) { onError(ex); }

        finally { running = false; Refresh(); }

    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

}



public sealed class FieldEditor : Observable

{

    private readonly Field original;

    public Field ValueModel { get; }

    public FieldEditor(Field field, Field? baseline = null)

    {

        original = PackageJson.Clone(baseline ?? field); ValueModel = PackageJson.Clone(field);

        Restore = new ActionCommand(() => { Name = original.Name; Value = original.Value; Enabled = original.Enabled; });

    }

    public string Name { get => ValueModel.Name; set { ValueModel.Name = value; Changed(); Changed(nameof(IsChanged)); } }

    public string Value { get => ValueModel.Value; set { ValueModel.Value = value; Changed(); Changed(nameof(IsChanged)); } }

    public bool Enabled { get => ValueModel.Enabled; set { ValueModel.Enabled = value; Changed(); Changed(nameof(IsChanged)); } }

    public bool Required { get => ValueModel.Required; set { ValueModel.Required = value; Changed(); } }

    public bool Sensitive { get => ValueModel.Sensitive; set { ValueModel.Sensitive = value; Changed(); Changed(nameof(OriginalLabel)); } }

    public bool IsChanged => Value != original.Value || Name != original.Name || Enabled != original.Enabled;

    public string OriginalLabel => $"Original: {original.Value}{(!original.Enabled ? " (disabled)" : "")}";

    public ActionCommand Restore { get; }

}



public sealed class CaseEditor : Observable

{

    public static RequestBodyKind[] BodyKinds { get; } = Enum.GetValues<RequestBodyKind>();

    private RequestBodyKind bodyKind;

    public RequestBodyKind BodyKind { get => bodyKind; set { Set(ref bodyKind, value); Changed(nameof(BodyTabIndex)); } }

    public int BodyTabIndex => BodyKind is RequestBodyKind.Multipart or RequestBodyKind.FormUrlEncoded ? 1 : 0;

    public bool UseCookies { get; set; }

    public string HttpVersion { get; set; } = "1.1";

    public string ProxyUrl { get; set; } = "";

    public string ClientCertificateThumbprint { get; set; } = "";

    public bool FollowRedirects { get; set; }

    public int MaxRedirects { get; set; } = 5;

    public System.Collections.ObjectModel.ObservableCollection<BodyPart> Parts { get; }

    public TestCase Baseline { get; }
    public bool IsHttpStep => Baseline.Kind == TestStepKind.Http;

    public System.Collections.ObjectModel.ObservableCollection<FieldEditor> Query { get; }

    public System.Collections.ObjectModel.ObservableCollection<FieldEditor> Headers { get; }

    public System.Collections.ObjectModel.ObservableCollection<Assertion> Checks { get; }

    public System.Collections.ObjectModel.ObservableCollection<DatabaseCheck> DatabaseChecks { get; }

    private bool fixtureRequired;

    public bool FixtureRequired { get => fixtureRequired; set { if (Set(ref fixtureRequired, value)) Changed(nameof(FixtureLabel)); } }

    private string fixtureQueryId;

    public string FixtureQueryId { get => fixtureQueryId; set { if (Set(ref fixtureQueryId, value ?? "")) { Fixture = null; Changed(nameof(FixtureLabel)); } } }

    public FixtureSelection? Fixture { get; private set; }

    private RequestDefinition? resolvedOriginal;

    public string FixtureLabel => FixtureQueryId.Length == 0 ? "No database fixture required" : Fixture is null ? (FixtureRequired ? "A database fixture is required" : "Optional fixture · send with manual values or select a row") : $"Fixture row {Fixture.RowIndex + 1} · captured {Fixture.Evidence.Result.StartedAt:HH:mm:ss} UTC";

    private string method, url, body, contentType, title, instructions, status = "NOT RUN";

    private bool sendBody, bodySensitive;

    private int timeoutSeconds;

    public string IdentityRef { get; set; }

    public CaseEditor(TestCase definition)

    {

        Baseline = PackageJson.Clone(definition);

        BodyKind = definition.Request.BodyKind; UseCookies = definition.Request.UseCookies; Parts = new(PackageJson.Clone(definition.Request.Parts));

        HttpVersion = definition.Request.HttpVersion; ProxyUrl = definition.Request.ProxyUrl; ClientCertificateThumbprint = definition.Request.ClientCertificateThumbprint; FollowRedirects = definition.Request.FollowRedirects; MaxRedirects = definition.Request.MaxRedirects;

        title = definition.Title; instructions = definition.Instructions;

        method = definition.Request.Method; url = definition.Request.Url; body = definition.Request.Body;

        contentType = definition.Request.ContentType; sendBody = definition.Request.SendBody; bodySensitive = definition.Request.BodySensitive;

        timeoutSeconds = definition.Request.TimeoutSeconds;

        IdentityRef = definition.Request.IdentityRef;

        fixtureQueryId = definition.FixtureQueryId; fixtureRequired = definition.FixtureRequired;

        DatabaseChecks = new(PackageJson.Clone(definition.DatabaseChecks));

        Query = new(definition.Request.Query.Select(f => new FieldEditor(f)));

        Headers = new(definition.Request.Headers.Select(f => new FieldEditor(f)));

        Checks = new(PackageJson.Clone(definition.Assertions));

    }

    public string Title { get => title; set => Set(ref title, value); }

    public string Instructions { get => instructions; set { if (Set(ref instructions, value)) { Changed(nameof(HasInstructions)); Changed(nameof(InstructionsSummary)); } } }

    /// <summary>True when the case carries tester instructions; the Instructions expander opens only then.</summary>
    public bool HasInstructions => !string.IsNullOrWhiteSpace(Instructions);

    /// <summary>First line of the instructions for the collapsed expander header.</summary>
    public string InstructionsSummary
    {
        get
        {
            if (!HasInstructions) return "No instructions for this case";
            var line = Instructions.Trim().Split('\n')[0].Trim('\r', ' ');
            return line.Length > 100 ? line[..100].TrimEnd() + "…" : line;
        }
    }

    public string Method { get => method; set => Set(ref method, value); }

    public string Url { get => url; set => Set(ref url, value); }

    public string Body { get => body; set { Set(ref body, value); Changed(nameof(BodyChanged)); } }

    public string ContentType { get => contentType; set => Set(ref contentType, value); }

    public bool SendBody { get => sendBody; set => Set(ref sendBody, value); }

    public bool BodySensitive { get => bodySensitive; set => Set(ref bodySensitive, value); }

    public int TimeoutSeconds { get => timeoutSeconds; set => Set(ref timeoutSeconds, value); }

    public string Status { get => status; set { if (Set(ref status, value)) Changed(nameof(Outcome)); } }

    /// <summary>Badge state for the case list: a current manual assessment overrides the automated verdict.</summary>
    public string Outcome
    {
        get
        {
            var parts = status.Split(" · ");
            var manual = parts.FirstOrDefault(p => p.StartsWith("MANUAL: ", StringComparison.Ordinal));
            var verdict = manual is not null && !parts.Contains("REVIEW AGAIN") ? manual["MANUAL: ".Length..] : parts[0];
            return verdict switch { "PASS" => "Pass", "FAIL" => "Fail", "INCONCLUSIVE" => "Inconclusive", _ => "NotRun" };
        }
    }

    public bool IsVariation => Baseline.ParentId is not null;

    public bool BodyChanged => Body != (resolvedOriginal ?? Baseline.Request).Body;

    public string OriginalBody => (resolvedOriginal ?? Baseline.Request).Body;

    public void ApplyFixture(FixtureSelection selection)

    {

        var resolved = ValueBindings.Request(Baseline.Request, selection);

        Fixture = PackageJson.Clone(selection); resolvedOriginal = resolved;

        Method = resolved.Method; Url = resolved.Url; Body = resolved.Body; ContentType = resolved.ContentType; SendBody = resolved.SendBody;

        Query.Clear(); foreach (var f in resolved.Query) Query.Add(new FieldEditor(f));

        Headers.Clear(); foreach (var f in resolved.Headers) Headers.Add(new FieldEditor(f));

        Changed(nameof(FixtureLabel)); Changed(nameof(BodyChanged)); Changed(nameof(OriginalBody));

    }

    public TestCase ExecutionBaseline() => Baseline with { FixtureQueryId = FixtureQueryId, FixtureRequired = FixtureRequired, DatabaseChecks = PackageJson.Clone(DatabaseChecks.ToList()) };

    public void RestoreDraft(TestCase draft)

    {

        Title = draft.Title; Instructions = draft.Instructions; Method = draft.Request.Method; Url = draft.Request.Url;

        Body = draft.Request.Body; ContentType = draft.Request.ContentType; SendBody = draft.Request.SendBody;

        BodySensitive = draft.Request.BodySensitive; TimeoutSeconds = draft.Request.TimeoutSeconds; IdentityRef = draft.Request.IdentityRef;

        BodyKind = draft.Request.BodyKind; UseCookies = draft.Request.UseCookies;

        HttpVersion = draft.Request.HttpVersion; ProxyUrl = draft.Request.ProxyUrl; ClientCertificateThumbprint = draft.Request.ClientCertificateThumbprint; FollowRedirects = draft.Request.FollowRedirects; MaxRedirects = draft.Request.MaxRedirects;

        Parts.Clear(); foreach (var part in draft.Request.Parts) Parts.Add(PackageJson.Clone(part));

        FixtureQueryId = draft.FixtureQueryId; FixtureRequired = draft.FixtureRequired;

        Query.Clear(); foreach (var field in draft.Request.Query) Query.Add(new(field, Baseline.Request.Query.FirstOrDefault(f => f.Id == field.Id)));

        Headers.Clear(); foreach (var field in draft.Request.Headers) Headers.Add(new(field, Baseline.Request.Headers.FirstOrDefault(f => f.Id == field.Id)));

        Checks.Clear(); foreach (var check in draft.Assertions) Checks.Add(PackageJson.Clone(check));

        DatabaseChecks.Clear(); foreach (var check in draft.DatabaseChecks) DatabaseChecks.Add(PackageJson.Clone(check));

    }

    public RequestDefinition Request(string identity) => new()

    {

        Method = Method, Url = Url, Body = Body, SendBody = SendBody, ContentType = ContentType, BodySensitive = BodySensitive,

        TimeoutSeconds = TimeoutSeconds, IdentityRef = identity,

        BodyKind = BodyKind, UseCookies = UseCookies, Parts = PackageJson.Clone(Parts.ToList()),

        HttpVersion = HttpVersion, ProxyUrl = ProxyUrl, ClientCertificateThumbprint = ClientCertificateThumbprint, FollowRedirects = FollowRedirects, MaxRedirects = MaxRedirects,

        Query = Query.Select(f => PackageJson.Clone(f.ValueModel)).ToList(), Headers = Headers.Select(f => PackageJson.Clone(f.ValueModel)).ToList()

    };

    public TestCase Definition(string? identity = null)

    {

        var request = Request(identity ?? IdentityRef);

        if (resolvedOriginal is not null)

        {

            if (request.Url == resolvedOriginal.Url) request.Url = Baseline.Request.Url;

            if (request.Body == resolvedOriginal.Body) request.Body = Baseline.Request.Body;

            foreach (var (current, resolved, source) in new[] { (request.Query, resolvedOriginal.Query, Baseline.Request.Query), (request.Headers, resolvedOriginal.Headers, Baseline.Request.Headers) })

                foreach (var field in current)

                    if (resolved.FirstOrDefault(f => f.Id == field.Id)?.Value == field.Value && source.FirstOrDefault(f => f.Id == field.Id) is { } original) field.Value = original.Value;

        }

        return Baseline with { Title = Title, Instructions = Instructions, Request = request, Assertions = PackageJson.Clone(Checks.ToList()), FixtureQueryId = FixtureQueryId, FixtureRequired = FixtureRequired, DatabaseChecks = PackageJson.Clone(DatabaseChecks.ToList()) };

    }

}



public sealed class IdentityEditor : Observable

{

    private string alias = "none";
    private string? browserProfileKey;
    internal string BrowserProfileKey { get => browserProfileKey ?? Alias; set => browserProfileKey = value; }
    internal string? RememberedOrigin { get; set; }
    internal bool IsSnapshot { get; set; }
    internal DateTimeOffset? ExpiresAt { get; set; }
    internal DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Alias { get => alias; init => alias = value; }
    internal void Rename(string value) { browserProfileKey ??= Alias; Set(ref alias, value, nameof(Alias)); Changed(nameof(IsConfigurable)); }

    public bool IsConfigurable => Alias != "none";

    private AuthKind kind;

    private string secret = "";

    public AuthKind Kind { get => kind; set { if (Set(ref kind, value)) { ExpiresAt = TokenLifetime.ExpiresAt(Secret, value, CapturedAt); ForgetBrowser(); } } }

    public string HeaderName { get; set; } = "Authorization";

    public string Prefix { get; set; } = "";

    public string Secret { get => secret; set { secret = value; CapturedAt = DateTimeOffset.UtcNow; ExpiresAt = TokenLifetime.ExpiresAt(value, Kind, CapturedAt); ForgetBrowser(); } }

    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "e2e-tester";

    public string Scope { get; set; } = "openid profile api offline_access";

    public string RedirectUri { get; set; } = "http://127.0.0.1:7890/callback/";

    private string expectedSubject = "";

    public string ExpectedSubject { get => expectedSubject; set => Set(ref expectedSubject, value); }

    public bool AutomaticRefresh { get; set; } = true;

    public bool AllowLocalHttp { get; set; }

    public Infrastructure.BrowserTokenSession? BrowserSession { get; private set; }

    public string SessionDescription => BrowserSession is null ? !IsConfigurable ? "No authentication" : Secret.Length == 0 ? "Not signed in · choose a sign-in method below" : $"Token stored · expires {ExpiresAt:u} · supplied identity is not independently verified" : BrowserSession.RequiresLogin ? "Session is no longer usable. Sign in again before sending." : $"{BrowserSession.Identity.DisplayName}\nSubject: {BrowserSession.Identity.Subject}\nIssuer: {BrowserSession.Identity.Issuer}\nAccess token expires: {BrowserSession.Identity.ExpiresAt:u}\nIdentity verified by signed OIDC ID token";

    public bool CanEnterToken => IsConfigurable && BrowserSession is null;

    public void InstallBrowser(Infrastructure.BrowserTokenSession session)

    {

        Kind = AuthKind.Bearer; secret = ""; BrowserSession = session;

        ExpectedSubject = session.Identity.Subject; RefreshSession();

    }

    public void ForgetBrowser() { BrowserSession = null; RefreshSession(); }

    public void RefreshSession() { Changed(nameof(SessionDescription)); Changed(nameof(CanEnterToken)); }

    public Task<IdentitySession> ResolveAsync(CancellationToken token)
    {
        if (BrowserSession is null && Secret.Length > 0 && ExpiresAt <= DateTimeOffset.UtcNow)
        { Secret = ""; throw new InvalidOperationException("This saved token has expired. Choose another identity or sign in again."); }
        return BrowserSession is null ? Task.FromResult(Session) : BrowserSession.GetIdentityAsync(Alias, AutomaticRefresh, token);
    }

    public IdentitySession Session => new(Alias, Kind, Secret, HeaderName, Prefix);

    public override string ToString() => Alias;

}

