using System.Text.Json;
namespace E2ETester.Application;

public static class TokenLifetime
{
    // A cache expiry hint only; decoding these claims does not authenticate the token.
    public static DateTimeOffset? ExpiresAt(string token, E2ETester.Core.AuthKind kind, DateTimeOffset capturedAt)
    {
        if (kind != E2ETester.Core.AuthKind.Bearer || string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
            if (json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)) return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidOperationException) { }
        return capturedAt.AddHours(24);
    }
}
