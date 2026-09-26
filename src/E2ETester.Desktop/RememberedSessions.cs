using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E2ETester.Core;

namespace E2ETester.Desktop;

internal sealed record RememberedIdentity(string Origin, string Alias, string BrowserProfileKey, AuthKind Kind, string Secret, string HeaderName, string Prefix, DateTimeOffset? CapturedAt = null, DateTimeOffset? ExpiresAt = null, bool IsSnapshot = false);

internal sealed class RememberedSessions(string directory)
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2ETester");
    internal static string DefaultDirectory => Path.Combine(Root, "Sessions");
    internal static string BrowserDirectory => Path.Combine(Root, "Browser"); // Keep existing cookies in place.
    private string FilePath => Path.Combine(directory, "identities.protected");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("E2ETester.Sessions.v1");
    internal static string Origin(string address) => new Uri(address).GetLeftPart(UriPartial.Authority);
    internal string SharedBrowserProfile(string address, string preferred) => Locked(() =>
    {
        var origin = Origin(address);
        var path = Path.Combine(directory, "browser-profiles.json");
        var profiles = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : new Dictionary<string, string>();
        if (profiles.TryGetValue(origin, out var existing)) return existing;
        // Adopt an existing identity profile so upgrading keeps its Microsoft/Swagger cookies.
        var candidates = ReadCore().Where(s => s.Origin == origin).OrderByDescending(s => s.CapturedAt)
            .Select(s => s.BrowserProfileKey).Append(preferred).Append("cloud-user").Distinct().ToList();
        var profile = candidates.FirstOrDefault(key => Directory.Exists(Path.Combine(BrowserDirectory, "EBWebView", SwaggerLoginWindow.ProfileName(origin, key)))) ?? candidates[0];
        profiles[origin] = profile;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(profiles)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return profile;
    });
    internal List<RememberedIdentity> Read()
    {
        List<RememberedIdentity> result = [];
        Update(entries => result = entries);
        return result;
    }
    internal void Update(Action<List<RememberedIdentity>> change) => Locked(() =>
    {
        var entries = ReadCore(); change(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var captured = entry.CapturedAt ?? DateTimeOffset.UtcNow;
            entries[i] = entry with { CapturedAt = captured, ExpiresAt = entry.ExpiresAt ?? E2ETester.Application.TokenLifetime.ExpiresAt(entry.Secret, entry.Kind, captured) };
        }
        entries.RemoveAll(e => e.Secret.Length > 0 && e.ExpiresAt <= DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(entries);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, FilePath, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    });
    private List<RememberedIdentity> ReadCore()
    {
        if (!File.Exists(FilePath)) return [];
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<List<RememberedIdentity>>(plain) ?? []; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private T Locked<T>(Func<T> action)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\E2ETester-Sessions-" + key);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Session storage is busy. Try again.");
        try { return action(); } finally { mutex.ReleaseMutex(); }
    }
}
