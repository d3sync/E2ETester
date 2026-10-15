using System.IO;
using System.Text.Json;
namespace E2ETester.Desktop;
internal sealed record UpdatePreferences(string Source = "https://e2e.dev.xpk.gr/releases/", bool CheckOnStartup = true)
{
    internal const string PrimarySource = "https://e2e.dev.xpk.gr/releases/";
    internal const string MirrorSource = "https://tester.dev.xpk.gr/releases/";
    internal static string DirectoryPath => Path.Combine(RememberedSessions.Root,"Updates");
    internal static string[] SourceCandidates(string source)
    {
        var primary=source.Trim();
        return string.Equals(primary,PrimarySource,StringComparison.OrdinalIgnoreCase)
            ? [primary,MirrorSource]
            : [primary];
    }
    internal static UpdatePreferences Load(string directory)
    {
        var path=Path.Combine(directory,"settings.json");
        if(File.Exists(path)) return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path)) ?? new();
        var legacy=Path.Combine(Path.GetDirectoryName(directory)!,"update-source.txt");
        return File.Exists(legacy)?new(File.ReadAllText(legacy).Trim()):new();
    }
    internal void Save(string directory)
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"settings.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
