using System.IO;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    private readonly SemaphoreSlim draftGate = new(1, 1);
    private string lastDraft = "";
    private bool draftRecoveryBlocked;
    private string savedWorkSignature = "";
    private string WorkSignature() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PackageJson.Write(new { Package = CurrentPackage(), Query = Database.CurrentQuery() }))));
    public bool HasUnsavedWork => WorkSignature() != savedWorkSignature;
    private void MarkWorkSaved() => savedWorkSignature = WorkSignature();
    public async Task<bool> PrepareToExitAsync(ExitChoice choice)
    {
        if (Busy || Database.Busy) return false;
        if (choice == ExitChoice.Cancel) return false;
        await RestorePreparationsAsync();
        if (choice == ExitChoice.Save) await SaveDraftExplicitlyAsync();
        return true;
    }
    private async Task SaveDraftExplicitlyAsync()
    {
        var signature = WorkSignature();
        await SaveDraftAsync();
        savedWorkSignature = signature;
    }
    public bool CanAuthor => !Busy && run is null;
    public string PackageDescription { get => package.Description; set { package.Description = value; Changed(); } }
    public AsyncCommand SaveDraft { get; private set; } = null!;
    public AsyncCommand RestoreDraft { get; private set; } = null!;
    public ActionCommand DuplicateCase { get; private set; } = null!;
    public ActionCommand DeleteCase { get; private set; } = null!;
    public ActionCommand MoveCaseUp { get; private set; } = null!;
    public ActionCommand MoveCaseDown { get; private set; } = null!;

    private void InitializeAuthoring()
    {
        SaveDraft = Command(async () => { await SaveDraftExplicitlyAsync(); Message = "Draft saved locally with all entered values."; });
        RestoreDraft = Command(RestoreDraftAsync);
        DuplicateCase = new(() =>
        {
            if (SelectedCase is null) return;
            var definition = SelectedCase.Definition() with { Id = Guid.NewGuid().ToString("N"), ParentId = null, Title = SelectedCase.Title + " · copy" };
            var editor = new CaseEditor(definition); Cases.Insert(Cases.IndexOf(SelectedCase) + 1, editor); SelectedCase = editor; NotifyRun();
        }, () => CanAuthor && SelectedCase is not null);
        DeleteCase = new(() =>
        {
            if (SelectedCase is null) return;
            if (IsSetupCase(SelectedCase)) { Message="The sign-in and remote-IP steps stay at the start of a suite."; return; }
            if (Cases.Any(c => c.Baseline.ParentId == SelectedCase.Baseline.Id)) { Message = "Remove this case's variations first, so their parent reference stays valid."; return; }
            if (Cases.Any(c => ValueBindings.Dependencies(c.Definition().Request).Contains(SelectedCase.Baseline.Id))) { Message = "Another case captures this case's response. Remove that binding first."; return; }
            var index = Cases.IndexOf(SelectedCase); Cases.Remove(SelectedCase);
            SelectedCase = Cases.ElementAtOrDefault(Math.Min(index, Cases.Count - 1)); NotifyRun();
        }, () => CanAuthor && SelectedCase is not null);
        MoveCaseUp = new(() => MoveCase(-1), () => CanAuthor && SelectedCase is not null && Cases.IndexOf(SelectedCase) > 0);
        MoveCaseDown = new(() => MoveCase(1), () => CanAuthor && SelectedCase is not null && Cases.IndexOf(SelectedCase) < Cases.Count - 1);
    }
    private void MoveCase(int direction)
    {
        if (SelectedCase is null) return;
        var selected = SelectedCase; var index = Cases.IndexOf(selected);
        if (IsSetupCase(selected) || IsSetupCase(Cases[index + direction])) { Message="Keep sign-in and remote-IP validation first."; return; }
        Cases.Move(index, index + direction);
        if (Cases.Any(c => ValueBindings.Dependencies(c.Definition().Request).Any(id => !Cases.TakeWhile(p => p != c).Any(p => p.Baseline.Id == id))))
        { Cases.Move(index + direction, index); Message = "Keep response-source cases before the cases that use them."; }
        SelectedCase = selected; NotifyRun();
    }
    private WorkspaceDraft CaptureDraft() => new()
    {
        RunId = run?.Id, SelectedCaseId = SelectedCase?.Baseline.Id,
        Baseline = package with { Cases = Cases.Select(c => PackageJson.Clone(c.Baseline)).ToList() },
        Edited = CurrentPackage(), QueryDraft = Database.CurrentQuery()
    };
    public async Task SaveDraftAsync()
    {
        await draftGate.WaitAsync();
        try
        {
            if (draftRecoveryBlocked) throw new InvalidOperationException("The saved draft could not be restored. Open a package or start a new run before replacing it.");
            var safe = CaptureDraft().Snapshot();
            var json = PackageJson.Write(safe);
            if (json == lastDraft) return;
            await store.SaveDraftAsync(safe, []); lastDraft = json;
        }
        finally { draftGate.Release(); }
    }
    private async Task RestoreDraftAsync()
    {
        Busy = true;
        draftRecoveryBlocked = true;
        try
        {
            var draft = await store.LoadDraftAsync();
            if (draft is null) { draftRecoveryBlocked = false; Message = "No saved draft yet. Edits are saved locally while you work."; return; }
            if (draft.Version != 1) throw new InvalidDataException("This draft version is not supported.");
            if (draft.Baseline.Cases.Count != draft.Edited.Cases.Count || draft.Edited.Cases.Any(c => draft.Baseline.Cases.Count(b => b.Id == c.Id) != 1)) throw new InvalidDataException("Draft case references are inconsistent.");
            TestRun? saved = null;
            if (draft.RunId is not null)
            {
                saved = await coordinator.ResumeAsync(draft.RunId) ?? throw new InvalidDataException("The draft's recorded run is missing.");
                if (!saved.Package.Cases.Select(c => c.Id).SequenceEqual(draft.Edited.Cases.Select(c => c.Id))) throw new InvalidDataException("The run has changed since this draft was saved. Open it from Saved runs instead.");
            }
            LoadPackage(draft.Baseline, false);
            package = draft.Edited with { Cases = draft.Baseline.Cases };
            run = saved; BaseUrl = draft.Edited.BaseUrl; Database.LoadQueries(draft.Edited.Queries);
            if (draft.QueryDraft is not null) Database.RestoreQueryDraft(draft.QueryDraft);
            foreach (var c in Cases)
            {
                c.RestoreDraft(draft.Edited.Cases.Single(d => d.Id == c.Baseline.Id));
                c.Status = CaseStatus(saved?.Attempts.LastOrDefault(a => a.CaseId == c.Baseline.Id));
            }
            // Reassign after restoring identity references so selection resolves the saved alias.
            SelectedCase = null; SelectedCase = Cases.FirstOrDefault(c => c.Baseline.Id == draft.SelectedCaseId) ?? Cases.FirstOrDefault();
            UpdateSwaggerUrlFromPackage();
            lastDraft = PackageJson.Write(CaptureDraft().Snapshot());
            MarkWorkSaved(); NotifyRun(); LoadAttempts();
            Message = Cases.Any(c => c.FixtureRequired) ? "Draft restored. Re-enter credentials and select required database fixtures before sending. Optional fixtures can be skipped when using manual values. No requests were replayed." : "Draft restored. Sign in or load saved credentials before sending authenticated requests. No requests were replayed.";
        }
        finally { Busy = false; }
    }
}
