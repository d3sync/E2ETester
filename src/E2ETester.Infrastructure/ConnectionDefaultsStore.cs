using System.Security.Cryptography;
using System.Text;
using E2ETester.Application;

namespace E2ETester.Infrastructure;

// Local, credential-free defaults, scoped by API origin so unrelated environments stay separate.
public sealed class ConnectionDefaultsStore(string directory)
{
    private string FileFor(string baseUrl) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(baseUrl).GetLeftPart(UriPartial.Authority)))) + ".json");
    public List<DatabaseConnectionDetails> Read(string baseUrl)
    {
        var path = FileFor(baseUrl);
        return File.Exists(path) ? PackageJson.Read<List<DatabaseConnectionDetails>>(File.ReadAllText(path)) : [];
    }
    public void Save(string baseUrl, DatabaseConnectionDetails details, bool remove = false)
    {
        var entries = Read(baseUrl);
        entries.RemoveAll(d => d.Alias.Equals(details.Alias, StringComparison.OrdinalIgnoreCase));
        if (!remove) entries.Add(details with { Username = "" });
        Directory.CreateDirectory(directory);
        var path = FileFor(baseUrl); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, PackageJson.Write(entries)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
