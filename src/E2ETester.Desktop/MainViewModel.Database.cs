using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    public DatabaseViewModel Database { get; } = new();
    public ActionCommand OpenDatabase { get; private set; } = null!;
    public ActionCommand AddDatabaseCheck { get; private set; } = null!;
    public AsyncCommand VerifyDatabase { get; private set; } = null!;
    public event Action? OpenDatabaseRequested;
    public string DatabaseEvidenceText => SelectedAttempt is null ? "Select an API attempt to inspect database evidence."
        : SelectedAttempt.DatabaseEvidence.Count == 0 ? "No database evidence attached yet. Required checks remain INCONCLUSIVE until verified."
        : string.Join("\n\n", SelectedAttempt.DatabaseEvidence.Select(e => $"{e.Result.StartedAt:O}\n{e.Query.Title} · {e.Query.ConnectionRef} · {e.Query.Provider}\n{e.Purpose} · {e.Assessment?.Verdict.ToString() ?? "Evidence only"}\n{e.Result.Rows.Count} rows · {e.Result.DurationMs} ms{(e.Result.Truncated ? " · INCOMPLETE" : "")}\n{e.Result.Error ?? e.Assessment?.Message}\n{PackageJson.Write(e.Result.Rows)}"));

    private ConnectionDefaultsStore? connectionDefaults;
    private void SeedDatabaseConnections()
    {
        if (connectionDefaults is null) return;
        try
        {
            var needed = package.Queries.Select(q => (Alias: q.ConnectionRef, q.Provider))
                .Concat(package.Cases.Concat(package.GuidedActions ?? []).Where(c => c.Preparation is { Kind: PreparationKind.DatabaseValue }).Select(c => (Alias: c.Preparation!.ConnectionRef, c.Preparation!.Provider))).ToHashSet();
            foreach (var details in connectionDefaults.Read(package.BaseUrl).Where(d => needed.Contains((d.Alias, d.Provider))))
            {
                if (Database.Connections.Any(c => c.Alias.Equals(details.Alias, StringComparison.OrdinalIgnoreCase))) continue;
                Database.Connections.Add(new ConnectionEditor { Alias = details.Alias, Provider = details.Provider,
                    ConnectionString = (details with { Username = "" }).Build(""), ReadOnlyCredentialsConfirmed = details.ReadOnlyAccount });
            }
            Database.SelectedConnection = Database.Connections.FirstOrDefault(c => c.Alias == Database.Draft.ConnectionRef) ?? Database.Connections.FirstOrDefault();
        }
        catch { Database.Message = "Remembered connection defaults could not be loaded. Edit or import a connection in Query Studio."; }
    }
    private void InitializeDatabase()
    {
        Database.ConnectionChangeRestriction = connection => Busy ? "Finish the current operation before changing connections."
            : preparationRecoveryUnreadable || preparations.Any(p => p.Status != "restored" && p.Plan.Kind == PreparationKind.DatabaseValue && p.Plan.ConnectionRef == connection.Alias)
            ? "Restore prepared changes before editing or deleting this connection." : null;
        OpenDatabase = new(() =>
        {
            Database.UseFixture = ApplyFixture;
            Database.AttachEvidence = SelectedAttempt is not null && run?.FinishedAt is null ? AttachExplorationAsync : null;
            if (SelectedCase?.FixtureQueryId is { Length: > 0 } id) Database.SelectQuery(id);
            OpenDatabaseRequested?.Invoke();
        }, () => !Busy);
        AddDatabaseCheck = new(() => SelectedCase?.DatabaseChecks.Add(new DatabaseCheck { QueryId = Database.Queries.FirstOrDefault()?.Id ?? "" }), () => !Busy && SelectedCase is not null && Database.Queries.Count > 0);
        VerifyDatabase = Command(VerifyDatabaseAsync, () => SelectedAttempt is not null && SelectedAttempt.DatabasePlans.Count > 0 && run?.FinishedAt is null);
    }

    public void ApplyFixture(FixtureSelection fixture)
    {
        if (SelectedCase is null) throw new InvalidOperationException("Select a test case first.");
        var definition = Database.Queries.FirstOrDefault(q => q.Id == SelectedCase.FixtureQueryId);
        if (definition is null || DatabaseQueries.Fingerprint(definition) != DatabaseQueries.Fingerprint(fixture.Evidence.Query)) throw new InvalidOperationException("The selected result must come from the saved fixture query assigned to this case.");
        SelectedCase.ApplyFixture(fixture);
        Message = "Fixture loaded as the original request values. You can now edit them or create a variation.";
    }
    private async Task AttachExplorationAsync(QueryEvidence evidence)
    {
        if (run is null || SelectedAttempt is null) throw new InvalidOperationException("Select an API attempt first.");
        await coordinator.AttachDatabaseEvidenceAsync(run, SelectedAttempt, evidence);
        RefreshDatabaseEvidence();
    }
    private async Task VerifyDatabaseAsync()
    {
        if (run is null || SelectedAttempt is null) return;
        Busy = true; cancellation = new();
        try
        {
            await coordinator.VerifyDatabaseAsync(run, SelectedAttempt, new DatabaseQueries(new DatabaseExecutor()), Database.Connections.Select(c => c.Profile).ToList(), cancellation.Token);
            RefreshDatabaseEvidence(); Message = "Database checks recorded. Earlier verification results remain in the report.";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
    private void RefreshDatabaseEvidence()
    {
        var selectedId = SelectedAttempt?.Id; LoadAttempts(); SelectedAttempt = Attempts.FirstOrDefault(a => a.Id == selectedId);
        if (SelectedCase is not null && run is not null) SelectedCase.Status = CaseStatus(run.Attempts.LastOrDefault(a => a.CaseId == SelectedCase.Baseline.Id));
        NotifyRun();
    }
}
