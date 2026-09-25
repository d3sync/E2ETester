using System.IO;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    public ActionCommand OpenGuidedActions { get; private set; } = null!;
    private void InitializeGuidedActions() => OpenGuidedActions = new(() =>
        new GuidedActionsWindow(this, package.GuidedActions ?? []) { Owner = PreparationOwner() }.ShowDialog(),
        () => !Busy && !Database.Busy);
    internal TestRun? GuidedActionSource => run;
    internal async Task<TestRun?> ChooseActionSourceAsync(Window owner)
    {
        string? selected = null;
        RunHistoryWindow? dialog = null;
        var history = CreateRunHistory(id => { selected = id; dialog!.Close(); });
        await history.LoadAsync();
        dialog = new RunHistoryWindow(history) { Owner = owner }; dialog.ShowDialog();
        if (selected is null) return null;
        var source = await store.LoadAsync(selected) ?? throw new InvalidOperationException("Saved run was not found.");
        if (source.Package.Id != package.Id || source.BaseUrl != BaseUrl) throw new InvalidOperationException("Choose a run of this package on the current API environment.");
        return source;
    }
    internal async Task<TestRun?> FindGuidedActionSourceAsync(RequestDefinition request)
    {
        if (!ValueBindings.Dependencies(request).Any()) return run;
        for (var offset = 0; offset < 1000; offset += 50)
        {
            var summaries = await store.ListAsync(offset, 50);
            foreach (var summary in summaries)
            {
                var candidate = await store.LoadAsync(summary.Id);
                if (candidate is null || candidate.Package.Id != package.Id || candidate.BaseUrl != BaseUrl) continue;
                if (ValueBindings.Dependencies(request).All(id => GuidedActionBindings.Candidates(candidate, id, request).Any())) return candidate;
            }
            if (summaries.Count < 50) break;
        }
        return null;
    }
    internal async Task<string> ExecuteGuidedRequestAsync(TestCase action, RequestDefinition request, TestRun? source)
    {
        if (Busy || Database.Busy) throw new InvalidOperationException("Wait for the current operation.");
        if (source is not null && (source.Package.Id != package.Id || source.BaseUrl != BaseUrl)) throw new InvalidOperationException("The source run must belong to this package and API environment.");
        var identityEditor = Identities.SingleOrDefault(i => i.Alias == request.IdentityRef) ?? throw new InvalidOperationException("Configure the selected identity first.");
        Busy = true; cancellation = new();
        try
        {
            var identity = await identityEditor.ResolveAsync(cancellation.Token);
            var resolved = GuidedActionBindings.Resolve(request, source);
            // The source run is immutable evidence; actions get their own durable run and never alter suite coverage.
            var single = PackageJson.Clone(action); single.Preparation = null; single.Request = resolved;
            var actionRun = await coordinator.StartAsync(new TestPackage { Title = "Guided Action · " + action.Title,
                Description = "Package: " + package.Id + "; source run: " + (source?.Id ?? "manual values"), BaseUrl = BaseUrl, Cases = [single] }, BaseUrl);
            var attempt = await coordinator.ExecuteAsync(actionRun, single, resolved, single.Assertions, identity, cancellation.Token);
            actionRun.FinishedAt = DateTimeOffset.UtcNow; await coordinator.SaveAsync(actionRun);
            return $"{attempt.Verdict} · saved action run {actionRun.Id}\nHTTP {attempt.Response?.StatusCode}\n{attempt.Response?.Error}\n{attempt.Response?.Body}";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; NotifyRun(); }
    }
    internal async Task ApplyGuidedPermissionsAsync(TestCase action, PreparationPlan plan, string identityRef)
    {
        if (Busy || Database.Busy || preparationRecoveryUnreadable) throw new InvalidOperationException("Finish active work or resolve recovery before changing permissions.");
        if (run?.FinishedAt is not null) throw new InvalidOperationException("Start a new run before applying temporary permissions. Cleanup requests can still use the finished run.");
        Preparations.Validate(plan);
        if (plan.Kind == PreparationKind.ApiValue && plan.RequireFreshLogin && (plan.CaptureRequest.IdentityRef == identityRef || plan.UpdateRequest.IdentityRef == identityRef))
            throw new InvalidOperationException("Use a separate administrator identity for permission capture and restoration.");
        var identity = Identities.SingleOrDefault(i => i.Alias == identityRef) ?? throw new InvalidOperationException("Choose the test identity whose permissions are being changed.");
        var previousToken = identity.Secret;
        Busy = true;
        try
        {
            await RestorePreparationsAsync();
            run ??= await coordinator.StartAsync(CurrentPackage(), BaseUrl);
            var entry = new PreparationRecovery { RunId = run.Id, PackageId = package.Id, CaseId = "$action:" + action.Id,
                BaseUrl = BaseUrl, IdentityRef = identityRef, PreviousTokenHash = TokenHash(previousToken), Plan = PackageJson.Clone(plan),
                ConnectionFingerprint = plan.Kind == PreparationKind.DatabaseValue ? TokenHash(Database.Connections.SingleOrDefault(c => c.Alias == plan.ConnectionRef)?.ConnectionString ?? "") : "" };
            var target = Target(entry);
            entry = entry with { OriginalJson = await target.ReadAsync(plan, default) };
            preparations.Add(entry);
            try { await Preparations.ApplyAsync(entry, target, PersistPreparations, default); }
            finally { await AuditPreparation(entry); }
            await InvalidatePreparedLogin(entry);
            Message = "Guided Action applied. Sign in again if required. Restore changes returns the captured original value.";
        }
        finally { Busy = false; Changed(nameof(PreparationStatus)); NotifyRun(); }
    }
}
