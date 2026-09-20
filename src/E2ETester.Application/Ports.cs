using E2ETester.Core;

namespace E2ETester.Application;

public interface IRunStore
{
    Task SaveAsync(TestRun run);
    Task<TestRun?> LoadLatestAsync();
}

public sealed record RunSummary(string Id, string Title, string BaseUrl, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int AttemptCount);

public interface IRunHistoryStore
{
    Task<IReadOnlyList<RunSummary>> ListAsync(int offset = 0, int limit = 50);
    Task<TestRun?> LoadAsync(string id);
}

public interface IHttpTransport
{
    Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken);
}

public interface IDatabaseExecutor
{
    Task<QueryResult> ExecuteAsync(QueryDefinition query, DatabaseConnectionProfile connection, CancellationToken cancellationToken);
}

public static class RequestUrls
{
    public static Uri Resolve(string baseUrl, RequestDefinition request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
                throw new InvalidDataException("Enter an HTTP or HTTPS base URL.");
            uri = new Uri(root, request.Url);
        }
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) throw new InvalidDataException("Use HTTP(S) without credentials in the URL.");
        if (uri.Fragment.Length > 0) throw new InvalidDataException("URL fragments are not sent to a server; remove the fragment.");
        var builder = new UriBuilder(uri);
        var query = request.Query.Where(f => f.Enabled).Select(f => $"{Uri.EscapeDataString(f.Name)}={Uri.EscapeDataString(f.Value)}");
        builder.Query = string.Join("&", new[] { builder.Query.TrimStart('?') }.Concat(query).Where(s => s.Length > 0));
        return builder.Uri;
    }
}
