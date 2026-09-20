using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using E2ETester.Application;

namespace E2ETester.Infrastructure;

/// <summary>Explicit, machine-local profile storage encrypted for the current Windows user.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedProfileStore(string directory)
{
    private string FilePath => Path.Combine(directory, "profiles.protected");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("E2ETester.LocalProfiles.v1");
    public async Task SaveAsync<T>(T profiles)
    {
        Directory.CreateDirectory(directory);
        var plain = Encoding.UTF8.GetBytes(PackageJson.Write(profiles));
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, encrypted); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<T?> LoadAsync<T>()
    {
        if (!File.Exists(FilePath)) return default;
        var encrypted = await File.ReadAllBytesAsync(FilePath);
        var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try { return PackageJson.Read<T>(Encoding.UTF8.GetString(plain)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Forget() { if (File.Exists(FilePath)) File.Delete(FilePath); }
}
