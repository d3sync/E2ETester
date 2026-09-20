using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;

public sealed class HttpTransport(int maxBodyBytes = 10 * 1024 * 1024) : IHttpTransport
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Net.CookieContainer> cookies = new();
    public async Task<ResponseSnapshot> SendAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        var watch = Stopwatch.StartNew(); var redirects = new List<RedirectHop>(); var secrets = new List<string>(); var requests = new List<SentRequest>();
        var current = request;
        while (true)
        {
            var response = await SendOneAsync(current, uri, identity, budget.Token); secrets.AddRange(response.SessionSecrets); requests.AddRange(response.Requests);
            ResponseSnapshot Finish(ResponseSnapshot value) => value with { Requests = requests, Redirects = redirects, DurationMs = watch.ElapsedMilliseconds, SessionSecrets = secrets, ErrorCode = value.ErrorCode == "cancelled" && !cancellationToken.IsCancellationRequested ? "timeout" : value.ErrorCode };
            if (!request.FollowRedirects || response.ErrorCode is not null || response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return Finish(response);
            var location = response.Headers.FirstOrDefault(h => h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
            if (location is null) return Finish(response);
            redirects.Add(new(response.StatusCode.Value, uri.AbsoluteUri, location));
            if (redirects.Count > request.MaxRedirects) return Finish(response with { ErrorCode = "redirect_limit", Error = "Redirect limit reached. No further request was sent." });
            if (!Uri.TryCreate(uri, location, out var next) || next.GetLeftPart(UriPartial.Authority) != uri.GetLeftPart(UriPartial.Authority) || next.UserInfo.Length > 0) return Finish(response with { ErrorCode = "cross_origin_redirect", Error = "Redirect to a different origin was not followed. Configure a separate case for that target." });
            if (response.StatusCode == 303 && current.Method != "HEAD" || response.StatusCode is 301 or 302 && current.Method == "POST") current = current with { Method = "GET", SendBody = false, Headers = current.Headers.Where(h => !h.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)).ToList() };
            uri = next;
        }
    }
    private async Task<ResponseSnapshot> SendOneAsync(RequestDefinition request, Uri uri, IdentitySession identity, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        int? status = null;
        var headers = new Dictionary<string, string[]>();
        var requests = new List<SentRequest>(); var sessionSecrets = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        try
        {
            var session = request.SessionScope + "|" + uri.GetLeftPart(UriPartial.Authority) + "|" + identity.Alias + "|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity.Secret)));
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = request.UseCookies, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli };
            if (request.ProxyUrl.Length > 0) handler.Proxy = new System.Net.WebProxy(request.ProxyUrl);
            if (request.ClientCertificateThumbprint.Length > 0)
            {
                using var certificates = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
                certificates.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var matches = certificates.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, request.ClientCertificateThumbprint, true);
                var certificate = matches.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey) ?? throw new InvalidOperationException("The client certificate is unavailable.");
                handler.ClientCertificates.Add(certificate);
            }
            if (request.UseCookies) handler.CookieContainer = cookies.GetOrAdd(session, _ => new());
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri) { Version = Version.Parse(request.HttpVersion), VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            if (request.SendBody)
            {
                if (request.BodyKind == RequestBodyKind.FormUrlEncoded)
                    message.Content = new FormUrlEncodedContent(request.Parts.Where(p => p.Enabled).Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));
                else if (request.BodyKind == RequestBodyKind.Multipart)
                {
                    var multipart = new MultipartFormDataContent(); message.Content = multipart;
                    foreach (var part in request.Parts.Where(p => p.Enabled))
                    {
                        var content = new ByteArrayContent(part.Binary ? Convert.FromBase64String(part.Value) : Encoding.UTF8.GetBytes(part.Value));
                        content.Headers.ContentType = MediaTypeHeaderValue.Parse(part.FileName.Length == 0 && !part.Binary ? "text/plain; charset=utf-8" : part.ContentType);
                        if (part.FileName.Length > 0) multipart.Add(content, part.Name, part.FileName); else multipart.Add(content, part.Name);
                    }
                }
                else
                {
                    message.Content = new ByteArrayContent(request.BodyKind == RequestBodyKind.Binary ? Convert.FromBase64String(request.Body) : Encoding.UTF8.GetBytes(request.Body));
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
                }
            }
            foreach (var field in request.Headers.Where(f => f.Enabled))
            {
                if (field.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || field.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Content-Length and Transfer-Encoding are controlled by the transport.");
                if (field.Name.Contains('\r') || field.Name.Contains('\n') || field.Value.Contains('\r') || field.Value.Contains('\n'))
                    throw new InvalidDataException("Header names and values must not contain line breaks.");
                if (!message.Headers.TryAddWithoutValidation(field.Name, field.Value))
                {
                    if (message.Content is null) throw new InvalidDataException($"Content header '{field.Name}' requires Send body.");
                    message.Content.Headers.Remove(field.Name);
                    if (!message.Content.Headers.TryAddWithoutValidation(field.Name, field.Value)) throw new InvalidDataException($"Unsupported header '{field.Name}'.");
                }
            }
            if (identity.Kind != AuthKind.None)
            {
                var name = identity.Kind == AuthKind.Bearer ? "Authorization" : identity.HeaderName;
                var value = identity.Kind == AuthKind.Bearer ? "Bearer " + identity.Secret : identity.Prefix + identity.Secret;
                message.Headers.Add(name, value);
            }
            var bodyBytes = message.Content is null ? [] : await message.Content.ReadAsByteArrayAsync(timeout.Token);
            if (message.Content is not null) message.Content.Headers.ContentLength = bodyBytes.Length;
            if (!message.Headers.Contains("Accept-Encoding")) message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            var sentHeaders = message.Headers.Concat(message.Content?.Headers.AsEnumerable() ?? []).ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            if (!sentHeaders.ContainsKey("Host")) sentHeaders["Host"] = [uri.Authority];
            if (request.UseCookies)
            {
                var cookieHeader = handler.CookieContainer.GetCookieHeader(uri);
                sessionSecrets.AddRange(handler.CookieContainer.GetCookies(uri).Cast<System.Net.Cookie>().Select(c => c.Value));
                if (cookieHeader.Length > 0) sentHeaders["Cookie"] = [string.Join("; ", (sentHeaders.GetValueOrDefault("Cookie") ?? []).Append(cookieHeader))];
            }
            var binaryBody = request.BodyKind is RequestBodyKind.Binary or RequestBodyKind.Multipart;
            requests.Add(new SentRequest { Method = message.Method.Method, Url = uri.AbsoluteUri, HttpVersion = message.Version.ToString(), Headers = sentHeaders, HasBody = message.Content is not null, BodyBytes = bodyBytes.Length, BodyEncoding = binaryBody ? "base64" : "text", Body = binaryBody ? Convert.ToBase64String(bodyBytes) : Encoding.UTF8.GetString(bodyBytes) });
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            status = (int)response.StatusCode;
            headers = response.Headers.Concat(response.Content.Headers).ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (buffer.Length <= maxBodyBytes)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maxBodyBytes + 1 - (int)buffer.Length)), timeout.Token);
                if (read == 0) break;
                buffer.Write(chunk, 0, read);
            }
            var truncated = buffer.Length > maxBodyBytes;
            var bytes = buffer.ToArray();
            var encoding = Encoding.UTF8;
            try { if (response.Content.Headers.ContentType?.CharSet is string charset) encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (ArgumentException) { }
            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            var binary = media.Length > 0 && !media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !media.Contains("json", StringComparison.OrdinalIgnoreCase) && !media.Contains("xml", StringComparison.OrdinalIgnoreCase) && !media.Contains("javascript", StringComparison.OrdinalIgnoreCase) && !media.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
            return new ResponseSnapshot
            {
                StatusCode = status, Headers = headers, Body = binary ? Convert.ToBase64String(bytes, 0, Math.Min(bytes.Length, maxBodyBytes)) : encoding.GetString(bytes, 0, Math.Min(bytes.Length, maxBodyBytes)), BodyEncoding = binary ? "base64" : "text",
                HttpVersion = response.Version.ToString(),
                CapturedBytes = Math.Min(bytes.Length, maxBodyBytes), Truncated = truncated, DurationMs = watch.ElapsedMilliseconds,
                Requests = requests,
                SessionSecrets = sessionSecrets.Concat(request.UseCookies ? handler.CookieContainer.GetCookies(uri).Cast<System.Net.Cookie>().Select(c => c.Value) : []).ToList()
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(cancellationToken.IsCancellationRequested ? "cancelled" : "timeout", "Waiting stopped. The server may have processed the request; it was not retried.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return Failure(ex is HttpRequestException ? "transport_error" : "request_error", ex is InvalidDataException ? ex.Message : "The request could not be completed. Check the URL, headers, connection and TLS configuration.");
        }
        ResponseSnapshot Failure(string code, string error) => new() { Requests = requests, SessionSecrets = sessionSecrets, StatusCode = status, Headers = headers, DurationMs = watch.ElapsedMilliseconds, ErrorCode = code, Error = error };
    }
}
