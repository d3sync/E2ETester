using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    private ProtectedProfileStore preparationStore = null!;
    private List<PreparationRecovery> preparations = [];
    private bool preparationRecoveryUnreadable;
    public AsyncCommand PrepareCase { get; private set; } = null!;
    public AsyncCommand RestorePreparation { get; private set; } = null!;
    public ActionCommand ConfigurePreparation { get; private set; } = null!;
    public ActionCommand ReuseTestIdentity { get; private set; } = null!;
    public bool HasPendingPreparation => preparationRecoveryUnreadable || preparations.Any(p => p.Status != "restored");
    public bool HasCasePreparation => SelectedCase?.Baseline.Preparation is not null;
    public string PreparationStatus => preparationRecoveryUnreadable ? "Preparation recovery could not be read. Restore access to the workspace before continuing."
        : preparations.Any(p => p.Status != "restored") ? "Prepared changes are active. Sign in again if required. Restore changes before finishing."
        : SelectedCase?.Baseline.Preparation is { } plan ? plan.Title + " · select Prepare before sending." : "No preparation required for this case.";

    private void InitializePreparation(string workspace)
    {
        preparationStore = new(Path.Combine(workspace, "Preparation"));
        ReuseTestIdentity = new(() =>
        {
            var identity = SelectedIdentity!;
            foreach (var test in Cases.Where(c => c.IdentityRef != "none")) test.IdentityRef = identity.Alias;
            Message = "All authenticated test cases now use '" + identity.Alias + "'. Preparation API administrator identities are unchanged. Browser cookies stay with this identity.";
        }, () => !Busy && SelectedIdentity?.IsConfigurable == true && !preparations.Any(p => p.Status != "restored"));
        try { preparations = Task.Run(() => preparationStore.LoadAsync<List<PreparationRecovery>>()).GetAwaiter().GetResult() ?? []; }
        catch { preparationRecoveryUnreadable = true; }
        ConfigurePreparation = new(() =>
        {
            if (SelectedCase is null) return;
            var dialog = new PreparationWindow(SelectedCase.Baseline.Preparation) { Owner = PreparationOwner() };
            if (dialog.ShowDialog() == true) { SelectedCase.Baseline.Preparation = dialog.Plan; Changed(nameof(PreparationStatus)); RefreshCommands(); }
        }, () => !Busy && run is null && SelectedCase is not null && !HasPendingPreparation);
        PrepareCase = Command(PrepareCaseAsync, () => SelectedCase?.Baseline.Preparation is not null && run?.FinishedAt is null);
        RestorePreparation = Command(RestorePreparationsAsync, () => preparations.Any(p => p.Status != "restored"));
    }
    private Window? PreparationOwner() => System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this);
    private Task PersistPreparations() => preparationStore.SaveAsync(preparations);
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private IPreparationTarget Target(PreparationRecovery entry)
    {
        if (entry.Plan.Kind == PreparationKind.DatabaseValue)
        {
            var profile = Database.Connections.SingleOrDefault(c => c.Alias == entry.Plan.ConnectionRef)?.Profile
                ?? throw new InvalidOperationException("Add the preparation's database connection in Query Studio first: " + entry.Plan.ConnectionRef);
            if (entry.ConnectionFingerprint.Length > 0 && TokenHash(profile.ConnectionString) != entry.ConnectionFingerprint)
                throw new InvalidOperationException("Restore the original database connection profile before restoring this preparation.");
            return new DatabasePreparationTarget(profile);
        }
        if (BaseUrl != entry.BaseUrl) throw new InvalidOperationException("Switch to the preparation's original base URL before restoring API changes.");
        return new ApiPreparationTarget(entry.BaseUrl, new HttpTransport(), async (alias, token) =>
        {
            var identity = Identities.SingleOrDefault(i => i.Alias == alias) ?? throw new InvalidOperationException("Configure the preparation API identity: " + alias);
            return await identity.ResolveAsync(token);
        }, evidence => entry.HttpEvidence.Add(evidence));
    }
    private void InvalidatePreparedLogin(PreparationRecovery entry)
    {
        if (!entry.Plan.RequireFreshLogin) return;
        var identity = Identities.FirstOrDefault(i => i.Alias == entry.IdentityRef);
        if (identity is null) return;
        identity.Secret = "";
        if (RememberedSessions.Origin(BaseUrl) == RememberedSessions.Origin(entry.BaseUrl)) RememberIdentity(identity);
        Changed(nameof(SelectedIdentity));
    }
    private async Task PrepareCaseAsync()
    {
        if (preparationRecoveryUnreadable) throw new InvalidOperationException(PreparationStatus);
        var selected = SelectedCase!;
        var plan = PackageJson.Clone(selected.Baseline.Preparation!);
        var dialog = new PreparationWindow(plan, execution: true) { Owner = PreparationOwner() };
        if (dialog.ShowDialog() != true) return;
        plan = dialog.Plan!; Preparations.Validate(plan);
        // Restoration must remain available after the tested identity loses its privileges.
        if (plan.Kind == PreparationKind.ApiValue && plan.RequireFreshLogin && (plan.CaptureRequest.IdentityRef == selected.IdentityRef || plan.UpdateRequest.IdentityRef == selected.IdentityRef))
            throw new InvalidOperationException("Use a separate administrator identity for API preparation so it can restore permissions after the test identity signs in again. Database preparation can use the same test identity.");
        Busy = true;
        try
        {
            var identity = SelectedIdentity is null ? "" : (await SelectedIdentity.ResolveAsync(default)).Secret;
            await RestorePreparationsAsync();
            if (run is null) run = await coordinator.StartAsync(CurrentPackage(), BaseUrl);
            var fingerprint = plan.Kind == PreparationKind.DatabaseValue ? TokenHash(Database.Connections.SingleOrDefault(c => c.Alias == plan.ConnectionRef)?.ConnectionString ?? "") : "";
            var entry = new PreparationRecovery { RunId = run.Id, PackageId = package.Id, CaseId = selected.Baseline.Id, BaseUrl = BaseUrl, IdentityRef = selected.IdentityRef, PreviousTokenHash = TokenHash(identity), ConnectionFingerprint = fingerprint, Plan = plan };
            var target = Target(entry);
            entry = entry with { OriginalJson = await target.ReadAsync(plan, default) };
            preparations.Add(entry);
            await AuditPreparation(entry);
            try { await Preparations.ApplyAsync(entry, target, PersistPreparations, default); }
            catch { await AuditPreparation(entry); throw; }
            InvalidatePreparedLogin(entry);
            await AuditPreparation(entry);
            Message = plan.RequireFreshLogin ? "Preparation applied. Sign in again using the same named identity, then send the request." : "Preparation applied and verified. Ready to send.";
        }
        catch
        {
            Message = "Preparation did not complete. Review pending changes and use Restore changes; a recovery record is retained if a write may have occurred.";
            throw;
        }
        finally { Busy = false; Changed(nameof(PreparationStatus)); RefreshCommands(); }
    }
    private async Task AuditPreparation(PreparationRecovery entry, string? outcome = null)
    {
        var owner = run?.Id == entry.RunId ? run : entry.RunId.Length > 0 ? await coordinator.ResumeAsync(entry.RunId) : null;
        if (owner is null) return;
        (owner.PreparationLog ??= []).Add(Preparations.Audit(entry, Identities.Select(i => i.Secret)) with { Status = outcome ?? entry.Status });
        await coordinator.SaveAsync(owner);
    }
    private async Task RestorePreparationsAsync()
    {
        if (preparationRecoveryUnreadable) throw new InvalidOperationException(PreparationStatus);
        var wasBusy = Busy; Busy = true;
        try
        {
            foreach (var entry in preparations.Where(p => p.Status != "restored").Reverse())
            {
                try { await Preparations.RestoreAsync(entry, Target(entry), PersistPreparations, default); }
                catch { await AuditPreparation(entry, "restoration_failed"); throw; }
                InvalidatePreparedLogin(entry); await AuditPreparation(entry);
            }
            Message = "Prepared changes restored and verified. Sign in again to refresh any affected JWT.";
        }
        finally { Busy = wasBusy; Changed(nameof(PreparationStatus)); RefreshCommands(); }
    }
    private void EnsurePreparationScope()
    {
        if (preparationRecoveryUnreadable) throw new InvalidOperationException(PreparationStatus);
        if (preparations.Any(p => p.Status != "restored" && (p.Status != "applied" || run is null || p.RunId != run.Id || p.PackageId != package.Id || p.CaseId != SelectedCase?.Baseline.Id || p.BaseUrl != BaseUrl || SelectedCase.Baseline.Preparation is null)))
            throw new InvalidOperationException("Restore prepared changes before sending outside their case and run.");
    }
    private void EnsurePreparationReady(IdentitySession identity)
    {
        EnsurePreparationScope();
        if (preparationRecoveryUnreadable) throw new InvalidOperationException(PreparationStatus);
        if (preparations.Any(p => p.Status == "pending")) throw new InvalidOperationException("Resolve pending preparation changes with Restore changes before sending.");
        if (SelectedCase?.Baseline.Preparation is not { } plan) return;
        var entry = preparations.LastOrDefault(p => p.Status == "applied" && p.PackageId == package.Id && p.CaseId == SelectedCase.Baseline.Id && p.BaseUrl == BaseUrl);
        if (entry is null) throw new InvalidOperationException("Select Prepare before sending this case.");
        if (PackageJson.Write(plan with { ValueJson = entry.Plan.ValueJson }) != PackageJson.Write(entry.Plan))
            throw new InvalidOperationException("Preparation configuration changed. Restore changes and prepare again before sending.");
        if (!plan.RequireFreshLogin) return;
        if (identity.Alias != entry.IdentityRef || string.IsNullOrWhiteSpace(identity.Secret) || TokenHash(identity.Secret) == entry.PreviousTokenHash)
            throw new InvalidOperationException("Sign in again using the same named identity to obtain a new JWT after preparation.");
        if (plan.JwtClaim.Length > 0)
        {
            try
            {
                var part = identity.Secret.Split('.')[1].Replace('-', '+').Replace('_', '/');
                using var jwt = JsonDocument.Parse(Convert.FromBase64String(part.PadRight((part.Length + 3) / 4 * 4, '=')));
                var claim = jwt.RootElement.GetProperty(plan.JwtClaim);
                var expected = Preparations.Scalar(entry.Plan.ValueJson);
                if (claim.ToString() != expected.ToString()) throw new InvalidOperationException("The refreshed JWT does not contain the prepared claim value. Sign in again before sending.");
            }
            catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException or KeyNotFoundException)
            { throw new InvalidOperationException("The new JWT must contain the claim configured by the preparation."); }
        }
    }
}
