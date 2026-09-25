using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using E2ETester.Core;

namespace E2ETester.Application;
public static partial class Reports
{
    public static async Task WriteBundleAsync(string path, TestRun run)
    {
        var report = Create(run);
        var files = new Dictionary<string, byte[]>
        {
            ["report.json"] = Encoding.UTF8.GetBytes(PackageJson.Write(report)),
            ["report.html"] = Encoding.UTF8.GetBytes(Html(report)),
            ["package.json"] = Encoding.UTF8.GetBytes(PackageJson.Write(report.Run.Package))
        };
        var manifest = new { schemaVersion = "1.0", runId = run.Id, exportedAt = report.ExportedAt, hashAlgorithm = "SHA-256", files = files.Select(f => new { path = f.Key, bytes = f.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.Value)) }).ToArray(), note = "Integrity hashes detect changes; they are not a digital signature. Evidence contains the captured values without redaction." };
        files["manifest.json"] = Encoding.UTF8.GetBytes(PackageJson.Write(manifest));
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in files)
                { await using var stream = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open(); await stream.WriteAsync(file.Value); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
