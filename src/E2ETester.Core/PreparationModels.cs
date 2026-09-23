namespace E2ETester.Core;

public enum PreparationKind { DatabaseValue, ApiValue }
public sealed record PreparationFlag(string Name, long Value);
public sealed record PreparationPlan
{
    public string Title { get; set; } = "Prepare test data";
    public PreparationKind Kind { get; set; }
    public string ConnectionRef { get; set; } = "test-admin-db";
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.MySql;
    public string Table { get; set; } = "";
    public string KeyColumn { get; set; } = "id";
    public string KeyJson { get; set; } = "1";
    public string ValueColumn { get; set; } = "permissions";
    public string ValueJson { get; set; } = "0";
    public List<PreparationFlag> Flags { get; set; } = [];
    public bool RequireFreshLogin { get; set; }
    public string JwtClaim { get; set; } = "";
    public RequestDefinition CaptureRequest { get; set; } = new();
    public string ValuePointer { get; set; } = "/permissions";
    public RequestDefinition UpdateRequest { get; set; } = new() { Method = "PATCH", SendBody = true, Body = "{\"permissions\":{{valueJson}}}" };
    public bool AllowUnconditionalApiUpdate { get; set; }
}

public sealed record PreparationRecovery
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CaseId { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string IdentityRef { get; init; } = "";
    public string PreviousTokenHash { get; init; } = "";
    public string ConnectionFingerprint { get; init; } = "";
    public PreparationPlan Plan { get; init; } = new();
    public string OriginalJson { get; init; } = "null";
    public string Status { get; set; } = "pending";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}
public sealed record PreparationAudit(DateTimeOffset At, string CaseId, string Title, string Status);
