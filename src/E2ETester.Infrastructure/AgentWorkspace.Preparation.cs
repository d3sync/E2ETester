using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;

public sealed partial class AgentWorkspace
{
    private sealed record EnvironmentEntry(string RunId, PreparationRecovery Recovery);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private async Task<List<EnvironmentEntry>> EnvironmentEntries()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return await new ProtectedProfileStore(Path.Combine(configuration.Workspace, "Preparation")).LoadAsync<List<EnvironmentEntry>>() ?? [];
    }
    private Task SaveEnvironment(List<EnvironmentEntry> entries)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("preparation_not_permitted");
        return new ProtectedProfileStore(Path.Combine(configuration.Workspace, "Preparation")).SaveAsync(entries);
    }
    private IPreparationTarget EnvironmentTarget(PreparationRecovery entry)
    {
        var plan = entry.Plan;
        if (plan.Kind == PreparationKind.DatabaseValue)
        {
            var db = configuration.Databases.SingleOrDefault(d => d.Alias == plan.ConnectionRef && d.Provider == plan.Provider && d.AllowPreparation)
                ?? throw new InvalidOperationException("preparation_not_permitted");
            var connection = EnvironmentValue(db.ConnectionEnvironmentVariable) ?? "";
            if (connection.Length == 0 || Hash(connection) != entry.ConnectionFingerprint) throw new InvalidOperationException("preparation_target_changed");
            return new DatabasePreparationTarget(new(db.Alias, db.Provider, connection, false));
        }
        if (!configuration.AllowHttp) throw new InvalidOperationException("http_not_permitted");
        foreach (var request in new[] { plan.CaptureRequest, plan.UpdateRequest })
        {
            Target(RequestUrls.Resolve(entry.BaseUrl, request));
            if (request.FollowRedirects || request.ProxyUrl.Length > 0 || request.ClientCertificateThumbprint.Length > 0 || request.Headers.Any(h => h.Enabled && h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("target_not_permitted");
        }
        return new ApiPreparationTarget(entry.BaseUrl, new HttpTransport(), (alias, _) => Identity(alias));
    }
    public async Task<object> ListPreparationsAsync()
    {
        await commands.WaitAsync();
        try { return (await EnvironmentEntries()).Select(e => new { e.RunId, operationId = e.Recovery.Id, e.Recovery.CaseId, e.Recovery.Status }).ToArray(); }
        finally { commands.Release(); }
    }
    // The caller supplies a stable ID. Once persisted, even uncertain writes cannot be replayed.
    public async Task<object> PrepareEnvironmentAsync(string runId, string caseId, string operationId, CancellationToken token)
    {
        await commands.WaitAsync(token);
        try
        {
            Idle();
            if (!configuration.AllowPreparation || !OperatingSystem.IsWindows()) throw new InvalidOperationException("preparation_not_permitted");
            if (!Guid.TryParse(operationId, out _)) throw new InvalidDataException("Use a UUID operation ID.");
            var entries = await EnvironmentEntries();
            var old = entries.SingleOrDefault(e => e.Recovery.Id == operationId);
            if (old is not null)
            {
                if (old.RunId != runId || old.Recovery.CaseId != caseId) throw new InvalidOperationException("stale_preparation");
                return new { operationId, state = old.Recovery.Status };
            }
            if (entries.Any(e => e.Recovery.Status != "restored")) throw new InvalidOperationException("restoration_required");
            var run = await GetRunAsync(runId);
            if (run.FinishedAt is not null) throw new InvalidOperationException("run_finished");
            var test = run.Package.Cases.Single(c => c.Id == caseId);
            var plan = test.Preparation ?? throw new InvalidOperationException("preparation_required");
            Preparations.Validate(plan);
            if (!configuration.ApprovedPreparations.Any(p => PackageJson.Write(p) == PackageJson.Write(plan))) throw new InvalidOperationException("preparation_not_permitted");
            if (plan.RequireFreshLogin && plan.Kind == PreparationKind.ApiValue && new[] { plan.CaptureRequest.IdentityRef, plan.UpdateRequest.IdentityRef }.Contains(test.Request.IdentityRef)) throw new InvalidOperationException("preparation_not_permitted");
            var identity = await Identity(test.Request.IdentityRef);
            var db = configuration.Databases.SingleOrDefault(d => d.Alias == plan.ConnectionRef);
            var entry = new PreparationRecovery { Id = operationId, CaseId = caseId, PackageId = run.Package.Id, BaseUrl = run.BaseUrl,
                IdentityRef = identity.Alias, PreviousTokenHash = Hash(identity.Secret), Plan = PackageJson.Clone(plan),
                ConnectionFingerprint = Hash(db is null ? "" : EnvironmentValue(db.ConnectionEnvironmentVariable) ?? "") };
            var target = EnvironmentTarget(entry);
            entry = entry with { OriginalJson = await target.ReadAsync(plan, token) };
            entries.Add(new(runId, entry));
            (run.PreparationLog ??= []).Add(new(DateTimeOffset.UtcNow, caseId, plan.Title, "pending"));
            await coordinator.SaveAsync(run);
            await Preparations.ApplyAsync(entry, target, () => SaveEnvironment(entries), token);
            if (plan.RequireFreshLogin) browserSessions.TryRemove(identity.Alias, out _);
            run.PreparationLog.Add(new(DateTimeOffset.UtcNow, caseId, plan.Title, "applied"));
            await coordinator.SaveAsync(run);
            return new { operationId, state = entry.Status, requiresFreshLogin = plan.RequireFreshLogin };
        }
        finally { commands.Release(); }
    }
    public async Task RestoreEnvironmentAsync(string runId, CancellationToken token)
    {
        await commands.WaitAsync(token);
        try { Idle(); await RestoreEnvironmentCoreAsync(await GetRunAsync(runId), token); }
        finally { commands.Release(); }
    }
    private async Task RestoreEnvironmentCoreAsync(TestRun run, CancellationToken token)
    {
        var entries = await EnvironmentEntries();
        foreach (var item in entries.Where(e => e.RunId == run.Id && e.Recovery.Status != "restored").Reverse())
        {
            var entry = item.Recovery;
            await Preparations.RestoreAsync(entry, EnvironmentTarget(entry), () => SaveEnvironment(entries), token);
            if (entry.Plan.RequireFreshLogin) browserSessions.TryRemove(entry.IdentityRef, out _);
            (run.PreparationLog ??= []).Add(new(DateTimeOffset.UtcNow, entry.CaseId, entry.Plan.Title, "restored"));
            await coordinator.SaveAsync(run);
        }
    }
    private async Task EnsureEnvironmentAsync(TestRun run, TestCase test, IdentitySession identity)
    {
        var pending = (await EnvironmentEntries()).Where(e => e.Recovery.Status != "restored").ToList();
        var match = pending.SingleOrDefault(e => e.RunId == run.Id && e.Recovery.CaseId == test.Id);
        if (pending.Any(e => e != match)) throw new InvalidOperationException("restoration_required");
        if (test.Preparation is null) return;
        var entry = match?.Recovery;
        if (entry is null || entry.Status != "applied" || entry.BaseUrl != run.BaseUrl || PackageJson.Write(entry.Plan) != PackageJson.Write(test.Preparation)) throw new InvalidOperationException("preparation_required");
        _ = EnvironmentTarget(entry);
        if (!entry.Plan.RequireFreshLogin) return;
        if (identity.Alias != entry.IdentityRef || string.IsNullOrWhiteSpace(identity.Secret) || Hash(identity.Secret) == entry.PreviousTokenHash) throw new InvalidOperationException("authentication_required");
        if (entry.Plan.JwtClaim.Length == 0) return;
        try
        {
            var part = identity.Secret.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using var jwt = JsonDocument.Parse(Convert.FromBase64String(part.PadRight((part.Length + 3) / 4 * 4, '=')));
            var claim = jwt.RootElement.GetProperty(entry.Plan.JwtClaim);
            if (!Preparations.Equal(claim.ValueKind == JsonValueKind.String ? claim.GetString()! : claim.GetRawText(), entry.Plan.ValueJson)) throw new InvalidDataException();
        }
        catch { throw new InvalidOperationException("authentication_required"); }
    }
}
