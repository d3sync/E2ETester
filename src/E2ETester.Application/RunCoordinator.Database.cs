using E2ETester.Core;

namespace E2ETester.Application;

public sealed partial class RunCoordinator
{
    public static void Recalculate(Attempt attempt)
    {
        attempt.Checks.RemoveAll(c => c.Id.StartsWith("db:", StringComparison.Ordinal));
        foreach (var plan in attempt.DatabasePlans)
        {
            var result = attempt.DatabaseEvidence.LastOrDefault(e => e.CheckId == plan.Check.Id)?.Assessment;
            attempt.Checks.Add(result ?? new CheckResult("db:" + plan.Check.Id, plan.Check.Required, Verdict.Inconclusive, plan.Check.ExpectedJson, "Pending", "Database verification has not run."));
        }
        attempt.Verdict = Outcomes.Evaluate(attempt.Checks);
        if (attempt.Response?.ErrorCode is not null && attempt.Verdict != Verdict.Fail) attempt.Verdict = Verdict.Inconclusive;
    }

    public async Task VerifyDatabaseAsync(TestRun run, Attempt attempt, DatabaseQueries queries, IReadOnlyList<DatabaseConnectionProfile> connections, CancellationToken cancellationToken)
    {
        if (!await execution.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("An operation is already running.");
        try
        {
            EnsureCanAttach(run, attempt);
            foreach (var plan in attempt.DatabasePlans)
            {
                var polling = System.Diagnostics.Stopwatch.StartNew();
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (plan.Check.PollTimeoutSeconds > 0) budget.CancelAfter(TimeSpan.FromSeconds(plan.Check.PollTimeoutSeconds));
                do
                {
                var pending = new QueryEvidence { Query = plan.Query, CheckId = plan.Check.Id, Purpose = "verification", Result = new QueryResult { ErrorCode = "running", Error = "Database verification started; no result has been recorded yet." } };
                pending = pending with { Assessment = DatabaseQueries.Assess(plan.Check, pending) };
                await AppendEvidenceAsync(run, attempt, pending);
                QueryEvidence evidence;
                try
                {
                    var connection = connections.SingleOrDefault(c => c.Alias == plan.Query.ConnectionRef) ?? throw new InvalidOperationException("The required database connection profile is not configured.");
                    evidence = await queries.ExecuteAsync(plan.Query, connection, budget.Token, attempt.Fixture,
                        attempt.Response is { Truncated: false, ErrorCode: null } ? attempt.Response.Body : null);
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
                {
                    evidence = new QueryEvidence { Query = plan.Query, Result = new QueryResult { ErrorCode = "prerequisite_unavailable", Error = "Configure the required connection and ensure response/fixture bindings are available." } };
                }
                evidence = evidence with { Id = pending.Id, CheckId = plan.Check.Id, Purpose = "verification", Assessment = DatabaseQueries.Assess(plan.Check, evidence) };
                var evidenceIndex = attempt.DatabaseEvidence.FindIndex(e => e.Id == pending.Id);
                attempt.DatabaseEvidence[evidenceIndex] = evidence; Recalculate(attempt);
                try { await store.SaveAsync(run); }
                catch { attempt.DatabaseEvidence[evidenceIndex] = pending; Recalculate(attempt); throw; }
                if (plan.Check.PollTimeoutSeconds == 0 || evidence.Assessment?.Verdict != Verdict.Fail || budget.IsCancellationRequested) break;
                var remaining = TimeSpan.FromSeconds(plan.Check.PollTimeoutSeconds) - polling.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(plan.Check.PollIntervalMilliseconds, remaining.TotalMilliseconds)), budget.Token); }
                catch (OperationCanceledException) { break; }
                } while (!budget.IsCancellationRequested);
                if (cancellationToken.IsCancellationRequested) break;
            }
        }
        finally { execution.Release(); }
    }

    public async Task AttachDatabaseEvidenceAsync(TestRun run, Attempt attempt, QueryEvidence evidence)
    {
        if (!await execution.WaitAsync(0)) throw new InvalidOperationException("An operation is already running.");
        try
        {
            EnsureCanAttach(run, attempt);
            // Exploration never satisfies a planned check; only the verification command can assign a CheckId.
            var safe = DatabaseQueries.SafeEvidence(evidence.Query, evidence.Result) with { Purpose = "exploration" };
            await AppendEvidenceAsync(run, attempt, safe);
        }
        finally { execution.Release(); }
    }

    private static void EnsureCanAttach(TestRun run, Attempt attempt)
    {
        if (run.FinishedAt is not null) throw new InvalidOperationException("This run is finished; its report is immutable.");
        if (!run.Attempts.Contains(attempt) || attempt.State == AttemptState.Running) throw new InvalidOperationException("Select a completed attempt from this run.");
    }
    private async Task AppendEvidenceAsync(TestRun run, Attempt attempt, QueryEvidence evidence)
    {
        attempt.DatabaseEvidence.Add(PackageJson.Clone(evidence)); Recalculate(attempt);
        try { await store.SaveAsync(run); }
        catch { attempt.DatabaseEvidence.RemoveAt(attempt.DatabaseEvidence.Count - 1); Recalculate(attempt); throw; }
    }
}
