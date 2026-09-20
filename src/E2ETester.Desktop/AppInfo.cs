using System.IO;
using System.Reflection;
namespace E2ETester.Desktop;
public static class AppInfo
{
    public const string Name = "E2E Tester";
    public const string Creator = "d3sync";
    public const string Website = "https://d3sync.org";
    public const string Copyright = "Copyright (c) 2026 d3sync and E2E Tester contributors";
    public static string Build => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public static string Version => Build.Split('+')[0];
    public static string VersionLabel => "v" + Version;
    public static string Details => $"{Name} {VersionLabel}\nGuided API testing for Windows\nCreated by {Creator}\n{Website}\n{Copyright}\nLicensed under the MIT License\nBuild: {Build}\nRuntime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
    public static string ReadNotice(string name)
    {
        using var reader = new StreamReader(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/About/" + name)).Stream);
        return reader.ReadToEnd();
    }
}
