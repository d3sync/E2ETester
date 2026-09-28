using System.Security.Cryptography;
using System.Text;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Infrastructure;

/// <summary>Connections explicitly remembered by the current Windows user, scoped to the API origin.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SavedConnections(string directory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("E2ETester.Connections.v1");
    private string PathFor(string origin) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(origin).GetLeftPart(UriPartial.Authority)))) + ".protected");
    public List<DatabaseConnectionProfile> Read(string origin)
    {
        var path = PathFor(origin);
        if (!File.Exists(path)) return [];
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return PackageJson.Read<List<DatabaseConnectionProfile>>(Encoding.UTF8.GetString(plain)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(string origin, DatabaseConnectionProfile connection, bool remove)
    {
        var path = PathFor(origin);
        using var mutex = new Mutex(false, "Local\\E2ETester-Connections-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))));
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Saved connections are busy. Try again.");
        try
        {
            var entries = Read(origin); entries.RemoveAll(c => c.Alias.Equals(connection.Alias, StringComparison.OrdinalIgnoreCase));
            if (!remove) entries.Add(connection);
            Directory.CreateDirectory(directory);
            var plain = Encoding.UTF8.GetBytes(PackageJson.Write(entries)); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); File.Move(temporary, path, true); }
            finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { mutex.ReleaseMutex(); }
    }
}
