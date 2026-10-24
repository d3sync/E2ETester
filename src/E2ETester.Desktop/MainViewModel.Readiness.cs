using System.IO;
using System.Windows.Input;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

/// <summary>
/// The readiness line above Send and the single Auth action: a read-only projection of what Send will check.
/// It mirrors <see cref="SendAsync"/>, <see cref="EnsurePreparationReady"/> and <see cref="RunCoordinator.Prerequisites"/>
/// on snapshots only: no identity resolution, no network, no state change. Send remains the authority.
/// </summary>
public sealed partial class MainViewModel
{
    private string readinessText = "", readinessState = "Info", readinessDetail = "";
    private string? readinessActionLabel, authActionLabel, blockingCaseId;
    private ICommand? readinessAction, authAction;
    private bool authActionIsPrimary;
    private ActionCommand? selectBlockingCase;
    private IdentityEditor? observedIdentity;

    public string ReadinessText => readinessText;
    /// <summary>"Ready", "Blocked", or "Info" for advice such as an unverified database check or a finished run.</summary>
    public string ReadinessState => readinessState;
    /// <summary>Every readiness note, one per line, for the tooltip.</summary>
    public string ReadinessDetail => readinessDetail;
    public string? ReadinessActionLabel => readinessActionLabel;
    public ICommand? ReadinessAction => readinessAction;
    public bool HasReadinessAction => readinessAction is not null;
    public string? AuthActionLabel => authActionLabel;
    public ICommand? AuthAction => authAction;
    public bool HasAuthAction => authAction is not null;
    public bool AuthActionIsPrimary => authActionIsPrimary;
    /// <summary>Navigation only: selects the earlier case that still has to pass, exactly as clicking it in the list would.</summary>
    public ActionCommand SelectBlockingCase => selectBlockingCase ??= new(() =>
    {
        if (Cases.FirstOrDefault(c => c.Baseline.Id == blockingCaseId) is { } target) SelectedCase = target;
    }, () => !Busy && blockingCaseId is not null);

    private void ObserveIdentity(IdentityEditor? identity)
    {
        if (ReferenceEquals(observedIdentity, identity)) return;
        if (observedIdentity is not null) observedIdentity.PropertyChanged -= IdentityChanged;
        observedIdentity = identity;
        if (observedIdentity is not null) observedIdentity.PropertyChanged += IdentityChanged;
    }
    private void IdentityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IdentityEditor.SessionDescription)) RefreshReadiness();
    }

    private void RefreshReadiness()
    {
        if (Database is null) return; // still constructing
        var notes = new List<string>();
        var state = "Ready"; string? first = null, label = null, blockingCase = null; ICommand? action = null; var fixtureFlagged = false;
        void Block(string text, string? actionLabel = null, ICommand? command = null, string? caseId = null)
        {
            notes.Add(text);
            if (state == "Blocked") return;
            state = "Blocked"; first = text; label = actionLabel; action = command; blockingCase = caseId;
        }
        void Advise(string text, string actionLabel, ICommand? command)
        {
            notes.Add(text);
            if (state != "Ready") return;
            state = "Info"; label = actionLabel; action = command;
        }
        var editor = SelectedCase; var identity = SelectedIdentity;
        ObserveIdentity(identity);

        // Identity: the Auth tab's single action depends on this alone.
        string? authLabel = null; ICommand? auth = null; var authPrimary = false;
        if (identity is null || !identity.IsConfigurable) notes.Add("No authentication");
        else if (identity.BrowserSession is { } browser)
        {
            if (browser.RequiresLogin) { Block($"Needs: browser sign-in for '{identity.Alias}'", "Sign in with browser", LoginBrowser); authLabel = "Sign in with browser"; auth = LoginBrowser; authPrimary = true; }
            else { notes.Add($"Signed in as {browser.Identity.DisplayName} · expires {browser.Identity.ExpiresAt:HH:mm} UTC"); authLabel = "Forget login"; auth = ForgetLogin; }
        }
        else if (identity.Secret.Length == 0)
        {
            if (identity.Kind != AuthKind.Bearer) Block($"Needs: a token for '{identity.Alias}' · enter it under Auth");
            else { Block($"Needs: sign-in for '{identity.Alias}'", "Sign in with Swagger", OpenSwagger); authLabel = "Sign in with Swagger"; auth = OpenSwagger; authPrimary = true; }
        }
        else if (identity.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        { Block($"Token for '{identity.Alias}' has expired", "Sign in with Swagger", OpenSwagger); authLabel = "Sign in with Swagger"; auth = OpenSwagger; authPrimary = true; }
        else
        {
            notes.Add(identity.ExpiresAt is { } expires ? $"Token for '{identity.Alias}' · expires {expires:HH:mm} UTC" : $"Token for '{identity.Alias}'");
            authLabel = "Sign in again"; auth = OpenSwagger;
        }

        if (Busy) { Publish("Info", "Working…", null, null, null, ["Working…"], authLabel, auth, authPrimary); return; }
        if (run?.FinishedAt is not null && !IsPlayground)
        {
            const string finished = "Run finished · start a new run to send more requests";
            Publish("Info", finished, "New run", NewRun, null, [finished], authLabel, auth, authPrimary); return;
        }
        if (editor is null) { Publish("Info", "Select a case", null, null, null, ["Select a case"], authLabel, auth, authPrimary); return; }

        // Preparation scope (mirrors EnsurePreparationScope / EnsurePreparationReady).
        if (preparationRecoveryUnreadable) Block(PreparationStatus);
        else if (preparations.Any(p => p.Status == "pending")) Block("Needs: Restore changes · a preparation is still pending", "Restore changes", RestorePreparation);
        else if (PreparationOutOfScope()) Block("Needs: Restore changes · prepared changes belong to another case or run", "Restore changes", RestorePreparation);
        else if (preparations.Any(p => p.Status != "restored")) notes.Add("Prepared changes active · restore before finishing");

        // Engine prerequisites, in the order Send will evaluate them.
        var session = identity?.Session ?? new IdentitySession("none", AuthKind.None);
        try
        {
            foreach (var blocker in RunCoordinator.Prerequisites(Cases.Select(c => c.ExecutionBaseline()).ToList(), run?.Attempts ?? [], editor.ExecutionBaseline(), editor.IdentityRef, session, editor.Fixture, Database.Queries.ToList(), preparationConfirmed: true, bypassPrerequisites: RunAnyway))
                switch (blocker.Code)
                {
                    case "sign-in-step" or "remote-ip-step":
                        var title = Cases.FirstOrDefault(c => c.Baseline.Id == blocker.CaseId)?.Title ?? blocker.CaseId ?? "an earlier case";
                        Block($"Needs: '{title}' to pass first", "Go to that case", SelectBlockingCase, blocker.CaseId); break;
                    case "fixture-required" or "fixture-stale":
                        fixtureFlagged = true; Block("Needs: a database fixture row", "Select fixture…", OpenDatabase); break;
                    case "token-missing":
                        Block(blocker.Message, "Sign in with Swagger", OpenSwagger); break;
                    default:
                        Block(blocker.Message); break;
                }
        }
        catch (InvalidOperationException ex) { Block(ex.Message); }
        if (!fixtureFlagged && editor.Fixture is null && editor.Baseline.Kind == TestStepKind.Http)
        {
            var needsFixture = editor.FixtureRequired;
            if (!needsFixture) try { needsFixture = ValueBindings.RequiresFixture(editor.Request(session.Alias)); } catch (InvalidOperationException) { }
            if (needsFixture) Block("Needs: a database fixture row", "Select fixture…", OpenDatabase);
        }

        // Case preparation (mirrors EnsurePreparationReady without resolving identities).
        if (editor.Baseline.Preparation is { } plan)
        {
            if (PermissionIdentity.IsPermissionPlan(plan))
            {
                try
                {
                    var resolved = IdentityBindings.ResolveAsync(plan, alias => Task.FromResult(Identities.SingleOrDefault(i => i.Alias == alias)?.Session ?? throw new InvalidOperationException($"Sign in as source identity '{alias}' first."))).GetAwaiter().GetResult();
                    if (identity is not null && PermissionIdentity.Matches(resolved, session, identity.BrowserSession?.Identity.ExpiresAt ?? identity.ExpiresAt)) notes.Add($"Permissions ready · {resolved.JwtClaim} = {resolved.ValueJson}");
                    else Block($"Needs: a JWT with {resolved.JwtClaim} = {resolved.ValueJson} · Prepare, then sign in again", "Prepare…", PrepareCase);
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { Block("Preparation: " + ex.Message, "Prepare…", PrepareCase); }
            }
            else
            {
                var entry = preparations.LastOrDefault(p => p.Status == "applied" && p.PackageId == package.Id && p.CaseId == editor.Baseline.Id && p.BaseUrl == BaseUrl);
                if (entry is null) Block("Needs: Prepare before sending this case", "Prepare…", PrepareCase);
                else if (PackageJson.Write(plan with { ValueJson = entry.Plan.ValueJson }) != PackageJson.Write(entry.Plan)) Block("Preparation configuration changed · Restore changes, then Prepare again", "Restore changes", RestorePreparation);
                else
                {
                    try { CheckPreparedIdentity(entry, session); notes.Add("Preparation applied"); }
                    catch (InvalidOperationException ex) { Block(ex.Message, "Sign in with Swagger", OpenSwagger); }
                }
            }
        }
        else notes.Add("No preparation needed");

        // Applicability, only when every referenced identity has a readable JWT snapshot.
        if (editor.Baseline.AppliesWhen is { Count: > 0 } conditions)
        {
            if (RunAnyway) notes.Add("Run anyway · applicability and prerequisites bypassed for this request");
            else if (conditions.All(c => Identities.FirstOrDefault(i => i.Alias == c.IdentityRef) is { BrowserSession: null, Secret.Length: > 0 }))
            {
                var issues = IdentityBindings.ApplicabilityAsync(editor.Baseline, alias => Task.FromResult(Identities.Single(i => i.Alias == alias).Session)).GetAwaiter().GetResult();
                if (issues.Count > 0) Block("May not apply to this tester: " + issues[0] + " Tick Run anyway to override.");
                else notes.Add("Applies to this tester");
            }
            else notes.Add("Applicability is checked at send");
        }
        else if (RunAnyway) notes.Add("Run anyway · prerequisites bypassed for this request");

        // After a send: required database checks stay INCONCLUSIVE until verified.
        if (SelectedAttempt is { DatabasePlans.Count: > 0, DatabaseEvidence.Count: 0 } && VerifyDatabase?.CanExecute(null) == true)
            Advise("Database checks not verified yet · the result stays INCONCLUSIVE until you verify", "Verify database", VerifyDatabase);

        var ready = editor.Baseline.Kind == TestStepKind.SwaggerAuthentication ? "ready to complete the sign-in step" : "ready to send";
        var text = state switch
        {
            "Blocked" => first!,
            "Info" => string.Join(" · ", notes),
            _ => string.Join(" · ", notes.Append(ready))
        };
        Publish(state, text, label, action, blockingCase, notes, authLabel, auth, authPrimary);
    }

    private void Publish(string state, string text, string? label, ICommand? action, string? blockingCase, IEnumerable<string> notes, string? authLabel, ICommand? auth, bool authPrimary)
    {
        readinessState = state; readinessText = text; readinessActionLabel = label; readinessAction = action; blockingCaseId = blockingCase;
        readinessDetail = string.Join("\n", notes);
        authActionLabel = authLabel; authAction = auth; authActionIsPrimary = authPrimary;
        selectBlockingCase?.Refresh();
        foreach (var name in new[] { nameof(ReadinessText), nameof(ReadinessState), nameof(ReadinessDetail), nameof(ReadinessActionLabel), nameof(ReadinessAction), nameof(HasReadinessAction), nameof(AuthActionLabel), nameof(AuthAction), nameof(HasAuthAction), nameof(AuthActionIsPrimary) })
            Changed(name);
    }
}
