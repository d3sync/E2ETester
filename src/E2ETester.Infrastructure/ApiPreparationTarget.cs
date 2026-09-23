using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;

public sealed class ApiPreparationTarget(string baseUrl, IHttpTransport transport, Func<string, CancellationToken, Task<IdentitySession>> identity) : IPreparationTarget
{
    private async Task<ResponseSnapshot> Send(RequestDefinition request, CancellationToken token)
    {
        var response = await transport.SendAsync(request, RequestUrls.Resolve(baseUrl, request), await identity(request.IdentityRef, token), token);
        if (response.Error is not null || response.Truncated || response.StatusCode is not (>= 200 and < 300)) throw new InvalidOperationException("Preparation API request did not succeed. Check the administrator identity and endpoint; the request was not retried.");
        return response;
    }
    private static string Extract(ResponseSnapshot response, string pointer)
    {
        using var doc = JsonDocument.Parse(response.Body); var value = doc.RootElement;
        foreach (var segment in pointer.Split('/').Skip(1))
        {
            var name = segment.Replace("~1", "/").Replace("~0", "~");
            value = value.ValueKind == JsonValueKind.Array ? value[int.Parse(name, System.Globalization.CultureInfo.InvariantCulture)] : value.GetProperty(name);
        }
        var json = value.GetRawText(); Preparations.Scalar(json); return json;
    }
    public async Task<string> ReadAsync(PreparationPlan plan, CancellationToken token) { Preparations.Validate(plan); return Extract(await Send(plan.CaptureRequest, token), plan.ValuePointer); }
    public async Task ReplaceAsync(PreparationPlan plan, string expectedJson, string replacementJson, CancellationToken token)
    {
        Preparations.Validate(plan);
        var capture = await Send(plan.CaptureRequest, token);
        if (!Preparations.Equal(Extract(capture, plan.ValuePointer), expectedJson)) throw new InvalidOperationException("Preparation target changed. No update was sent.");
        var update = PackageJson.Clone(plan.UpdateRequest); update.Body = update.Body.Replace("{{valueJson}}", replacementJson);
        update.Headers.RemoveAll(h => h.Name.Equals("If-Match", StringComparison.OrdinalIgnoreCase));
        var etag = capture.Headers.FirstOrDefault(h => h.Key.Equals("ETag", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        if (!string.IsNullOrEmpty(etag)) update.Headers.Add(new Field { Name = "If-Match", Value = etag });
        else if (!plan.AllowUnconditionalApiUpdate) throw new InvalidOperationException("The capture API did not return an ETag. The package must explicitly allow updates without a concurrency condition if this API does not support one.");
        await Send(update, token);
        if (!Preparations.Equal(await ReadAsync(plan, token), replacementJson)) throw new InvalidOperationException("Preparation API update could not be verified. The recovery record is retained.");
    }
}
