using System.Diagnostics;
using System.Net;
using System.Text;
using Duende.IdentityModel.OidcClient.Browser;

namespace E2ETester.Infrastructure;

public sealed class LoopbackBrowser(Action<string>? launch = null) : IBrowser
{
    public static Uri ValidateRedirect(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port < 1024 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/callback/")
            throw new InvalidOperationException("Use a registered callback such as http://127.0.0.1:7890/callback/ (port 1024 or higher).");
        return uri;
    }

    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        var callback = ValidateRedirect(options.EndUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var listener = new HttpListener();
        listener.Prefixes.Add(callback.AbsoluteUri);
        listener.Start();
        (launch ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })))(options.StartUrl);
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var request = context.Request;
            var valid = request.HttpMethod == "GET" && request.Url?.GetLeftPart(UriPartial.Path) == callback.AbsoluteUri && request.RawUrl?.Length <= 16384;
            context.Response.StatusCode = valid ? 200 : 400;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var bytes = Encoding.UTF8.GetBytes(valid ? "Authentication response received. Return to E2E Tester to check the result." : "Invalid callback.");
            context.Response.ContentLength64 = bytes.Length;
            context.Response.KeepAlive = false;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
            if (valid) return new BrowserResult { ResultType = BrowserResultType.Success, Response = request.Url!.AbsoluteUri };
        }
    }
}
