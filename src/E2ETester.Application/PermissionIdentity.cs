using System.Text.Json;
using E2ETester.Core;
namespace E2ETester.Application;

public static class PermissionIdentity
{
    public static bool IsPermissionPlan(PreparationPlan? plan) => plan is { RequireFreshLogin: true, JwtClaim.Length: > 0 };

    // This compares supplied claims, not signatures. The API still validates the JWT on each request.
    public static bool Matches(PreparationPlan? plan, IdentitySession identity, DateTimeOffset? expiresAt = null)
    {
        if (!IsPermissionPlan(plan) || identity.Kind != AuthKind.Bearer || string.IsNullOrWhiteSpace(identity.Secret)) return false;
        var expiry = expiresAt ?? identity.Verified?.ExpiresAt ?? TokenLifetime.ExpiresAt(identity.Secret, identity.Kind, DateTimeOffset.UtcNow);
        if (expiry <= DateTimeOffset.UtcNow) return false;
        try
        {
            var part = identity.Secret.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using var jwt = JsonDocument.Parse(Convert.FromBase64String(part.PadRight((part.Length + 3) / 4 * 4, '=')));
            return jwt.RootElement.GetProperty(plan!.JwtClaim).ToString() == Preparations.Scalar(plan.ValueJson).ToString();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException or KeyNotFoundException or InvalidOperationException) { return false; }
    }
}
