using System.Text;
using E2ETester.Core;

namespace E2ETester.Application;
public static class RequestDiagnostics
{
    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
    public static string Curl(SentRequest request, int timeoutSeconds)
    {
        var output = new StringBuilder("# Bash / sh syntax. Secrets are redacted; replace placeholders locally.\n");
        output.Append("# ").AppendLine(request.Note);
        if (request.HasBody && request.BodyEncoding == "base64" && request.Body != Redaction.Mask)
            output.Append("printf '%s' ").Append(Quote(request.Body)).Append(" | base64 --decode | ");
        output.Append("curl --globoff --path-as-is --max-time ").Append(timeoutSeconds).Append(" --request ").Append(Quote(request.Method)).Append(" \\\n  --url ").Append(Quote(request.Url));
        foreach (var header in request.Headers)
            foreach (var value in header.Value) output.Append(" \\\n  --header ").Append(Quote(header.Key + ": " + value));
        if (request.HasBody) output.Append(" \\\n  --data-binary ").Append(request.BodyEncoding == "base64" && request.Body != Redaction.Mask ? "@-" : Quote(request.Body));
        return output.ToString();
    }
    public static string Describe(Attempt attempt)
    {
        if (attempt.Response?.Requests is not { Count: > 0 } requests)
            return "No transport request snapshot was recorded for this attempt (older record or failure before send).\n\n" + PackageJson.Write(new { attempt.EffectiveUrl, attempt.Request });
        return "TRANSPORT SETTINGS (cURL uses your local TLS/proxy configuration; certificate store settings are not portable)\n" + PackageJson.Write(new { attempt.Request.ProxyUrl, attempt.Request.ClientCertificateThumbprint, attempt.Request.FollowRedirects, attempt.Request.UseCookies }) + "\n\n" + string.Join("\n\n────────────────────────────────\n\n", requests.Select((r, i) => $"REQUEST {i + 1}\n{r.Method} {r.Url}\nHTTP preference: {r.HttpVersion} · body: {r.BodyBytes} bytes\n\n{Curl(r, attempt.Request.TimeoutSeconds)}\n\nRECORDED REQUEST\n{PackageJson.Write(r)}"));
    }
}
