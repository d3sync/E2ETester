namespace E2ETester.Application;

public static class SwaggerTokenCapture
{
    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.Query.Length > 0 || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidDataException("Use an HTTPS Swagger URL without credentials, query or fragment. HTTP is allowed only for loopback development.");
        return uri;
    }
    public static string? FromAuthorization(Uri swagger, string requestUrl, string authorization)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var request) || request.UserInfo.Length > 0 || request.GetLeftPart(UriPartial.Authority) != swagger.GetLeftPart(UriPartial.Authority)) return null;
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        return token.Length is > 0 and <= 65536 && !token.Any(char.IsWhiteSpace) ? token : null;
    }
}
