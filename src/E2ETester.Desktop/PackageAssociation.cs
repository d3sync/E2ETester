using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace E2ETester.Desktop;
internal static class PackageAssociation
{
    internal const string ProgId = "E2ETester.Package";
    internal static string Command(string executable) => $"\"{executable}\" \"%1\"";
    public static string Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        // Velopack's launcher remains stable when its current directory is replaced.
        var launcher = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "E2E Tester.exe"));
        if (Path.GetFileName(Path.GetDirectoryName(executable)) == "current" && File.Exists(launcher)) executable = launcher;
        using var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId);
        type.SetValue("", "E2E Tester test package");
        using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
        using (var command = type.CreateSubKey(@"shell\open\command")) command.SetValue("", Command(executable));
        using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.jsee");
        var current = extension.GetValue("") as string;
        if (string.IsNullOrEmpty(current) || current == ProgId) extension.SetValue("", ProgId);
        using (var openWith = extension.CreateSubKey("OpenWithProgids")) openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        using (var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\E2ETester\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "E2E Tester"); capabilities.SetValue("ApplicationDescription", "Guided API testing and .jsee packages");
            using var associations = capabilities.CreateSubKey("FileAssociations"); associations.SetValue(".jsee", ProgId);
        }
        using (var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications")) apps.SetValue("E2E Tester", @"Software\E2ETester\Capabilities");
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jsee\UserChoice");
        return choice?.GetValue("ProgId") is string selected && selected != ProgId
            ? ".jsee is registered. Windows currently uses another app; choose E2E Tester in Open with to change it."
            : ".jsee packages are registered for E2E Tester.";
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
