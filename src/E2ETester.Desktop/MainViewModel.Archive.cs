using System.IO;
using E2ETester.Application;

namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    private string? sourcePackageFileName;
    private string PackageFileName => sourcePackageFileName ?? Reports.FileStem(package.Title) + ".jsee";
    private void RememberPackageFile(string path)
    {
        sourcePackageFileName = Path.GetFileName(path);
        var directory = Path.Combine(workspacePath, "Packages"); Directory.CreateDirectory(directory);
        var copy = Path.Combine(directory, Reports.FileStem(sourcePackageFileName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".jsee");
        File.Copy(path, copy, false);
    }
    private async Task ArchiveRunPackageAsync()
    {
        if (run is null) return;
        var directory = Path.Combine(workspacePath, "Packages", run.Id); Directory.CreateDirectory(directory);
        var nameFile = Path.Combine(directory, "source-name.txt");
        if (!File.Exists(nameFile)) await Reports.WriteAtomicAsync(nameFile, PackageFileName);
        await Reports.WriteAtomicAsync(Path.Combine(directory, "package.jsee"), PackageJson.Write(run.Package));
    }
    internal async Task<(string Simple, string Full)> SaveLocalReportsAsync()
    {
        if (run is null) throw new InvalidOperationException("Start a run before generating reports.");
        await ArchiveRunPackageAsync();
        var source = await File.ReadAllTextAsync(Path.Combine(workspacePath, "Packages", run.Id, "source-name.txt"));
        return await Reports.WritePairAsync(Path.Combine(workspacePath, "Reports"), source, run);
    }
}
