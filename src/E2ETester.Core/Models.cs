namespace E2ETester.Core;

public enum TestStepKind { Http, SwaggerAuthentication }
public enum Verdict { Pass, Fail, Inconclusive }
public enum AttemptState { Running, Completed, Cancelled, Interrupted }
public enum AuthKind { None, Bearer, ApiToken }
public enum CheckKind { Status, BodyContains, JsonEquals, HeaderEquals, StatusIn, HeaderExists, JsonExists, JsonGreaterThan, JsonLessThan, JsonArrayCount, DurationLessThan, BodyNotEmpty, JsonSchema }
public enum RequestBodyKind { Text, FormUrlEncoded, Multipart, Binary }
public sealed record RedirectHop(int StatusCode, string Url, string Location);
public sealed record BodyPart
{
    public string Name { get; set; } = "field";
    public string Value { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public bool Binary { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; }
    public bool Sensitive { get; set; }
}

public sealed record Field
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; }
    public bool Sensitive { get; set; }
}

public sealed record RequestDefinition
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "/";
    public List<Field> Query { get; set; } = [];
    public List<Field> Headers { get; set; } = [];
    public string Body { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public bool SendBody { get; set; }
    public bool BodySensitive { get; set; }
    public RequestBodyKind BodyKind { get; set; }
    public List<BodyPart> Parts { get; set; } = [];
    public bool UseCookies { get; set; }
    public string HttpVersion { get; set; } = "1.1";
    public string ProxyUrl { get; set; } = "";
    public string ClientCertificateThumbprint { get; set; } = "";
    public bool FollowRedirects { get; set; }
    public int MaxRedirects { get; set; } = 5;
    [System.Text.Json.Serialization.JsonIgnore] public string SessionScope { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string, string> CaptureSources { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
    public string IdentityRef { get; set; } = "none";
}

public sealed record Assertion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CheckKind Kind { get; set; } = CheckKind.Status;
    public string Selector { get; set; } = "";
    public string Expected { get; set; } = "200";
    public bool Required { get; set; } = true;
}

public sealed record TestCase
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<IdentityClaimCondition>? AppliesWhen { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PreparationPlan? Preparation { get; set; }
    public TestStepKind Kind { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public string Title { get; set; } = "New test";
    public string Instructions { get; set; } = "Review the request and expected result, then send.";
    public RequestDefinition Request { get; set; } = new();
    public List<Assertion> Assertions { get; set; } = [];
    public string FixtureQueryId { get; set; } = "";
    public bool FixtureRequired { get; set; }
    public List<DatabaseCheck> DatabaseChecks { get; set; } = [];
}

public sealed record IdentityClaimCondition(string IdentityRef, string Claim, string ExpectedJson, string Description = "");

public sealed record TestPackage
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<TestCase>? GuidedActions { get; set; }
    public string SchemaVersion { get; set; } = "1.4";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Revision { get; set; } = 1;
    public string Title { get; set; } = "Untitled package";
    public string Description { get; set; } = "";
    public string BaseUrl { get; set; } = "http://localhost:5188";
    public List<TestCase> Cases { get; set; } = [];
    public List<QueryDefinition> Queries { get; set; } = [];
}

// Credentials live only in memory in this first increment; these objects never enter the journal.
public sealed record IdentitySession(string Alias, AuthKind Kind, string Secret = "", string HeaderName = "Authorization", string Prefix = "", VerifiedIdentity? Verified = null);
public sealed record CheckResult(string Id, bool Required, Verdict Verdict, string Expected, string Actual, string Message);
public sealed record FieldChange(string Path, string Original, string Current);
public sealed record ManualAssessment(string Id, DateTimeOffset CreatedAt, string Reviewer, Verdict Verdict,
    string Reason, Verdict AutomatedVerdict, string EvidenceHash, string ReviewerSource = "self_declared");

public sealed record ResponseSnapshot
{
    public List<SentRequest> Requests { get; init; } = [];
    public int? StatusCode { get; init; }
    public Dictionary<string, string[]> Headers { get; init; } = [];
    public string Body { get; init; } = "";
    public string BodyEncoding { get; init; } = "text";
    public string HttpVersion { get; init; } = "";
    public List<RedirectHop> Redirects { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public List<string> SessionSecrets { get; init; } = [];
    public bool Truncated { get; init; }
    public long CapturedBytes { get; init; }
    public long DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}

public sealed record SentRequest
{
    public string Method { get; init; } = "GET";
    public string Url { get; init; } = "";
    public string HttpVersion { get; init; } = "1.1";
    public Dictionary<string, string[]> Headers { get; init; } = [];
    public string Body { get; init; } = "";
    public string BodyEncoding { get; init; } = "text";
    public bool HasBody { get; init; }
    public long BodyBytes { get; init; }
    public string Note { get; init; } = "Request prepared for HttpClient; includes authentication, cookies and derived content headers. Not a packet capture. HTTP negotiation/proxy framing may differ.";
}

public sealed record Attempt
{
    public string? ExecutionId { get; init; }
    public Dictionary<string, string> CaptureSources { get; init; } = [];
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CaseId { get; init; } = "";
    public string CaseTitle { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public AttemptState State { get; set; } = AttemptState.Running;
    public Verdict Verdict { get; set; } = Verdict.Inconclusive;
    public string IdentityAlias { get; init; } = "none";
    public AuthKind AuthKind { get; init; }
    public VerifiedIdentity? VerifiedIdentity { get; init; }
    public string EffectiveUrl { get; init; } = "";
    public RequestDefinition Request { get; init; } = new();
    public List<FieldChange> Changes { get; init; } = [];
    public List<Assertion> Assertions { get; init; } = [];
    public ResponseSnapshot? Response { get; set; }
    public List<CheckResult> Checks { get; set; } = [];
    public FixtureSelection? Fixture { get; init; }
    public List<DatabaseCheckPlan> DatabasePlans { get; init; } = [];
    public List<QueryEvidence> DatabaseEvidence { get; set; } = [];
    public List<ManualAssessment> ManualAssessments { get; set; } = [];
}

public sealed record TestRun
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<PreparationAudit>? PreparationLog { get; set; }
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string PackageHash { get; init; } = "";
    public TestPackage Package { get; set; } = new();
    public string BaseUrl { get; init; } = "";
    public List<Attempt> Attempts { get; set; } = [];
}

public static class Outcomes
{
    public static Verdict Evaluate(IEnumerable<CheckResult> checks)
    {
        var required = checks.Where(c => c.Required).ToArray();
        if (required.Any(c => c.Verdict == Verdict.Fail)) return Verdict.Fail;
        return required.Length > 0 && required.All(c => c.Verdict == Verdict.Pass) ? Verdict.Pass : Verdict.Inconclusive;
    }

    public static Verdict ForRun(TestRun run)
    {
        var latest = run.Package.Cases.Select(c => run.Attempts.LastOrDefault(a => a.CaseId == c.Id)).ToArray();
        if (latest.Any(a => a?.Verdict == Verdict.Fail)) return Verdict.Fail;
        return latest.Length > 0 && latest.All(a => a?.State == AttemptState.Completed && a.Verdict == Verdict.Pass)
            ? Verdict.Pass : Verdict.Inconclusive;
    }
}
