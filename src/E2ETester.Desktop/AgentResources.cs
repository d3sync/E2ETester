using System.IO;
namespace E2ETester.Desktop;
internal static class AgentResources
{
    private static string Read(string name) { using var reader=new StreamReader(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Agent/"+name)).Stream); return reader.ReadToEnd(); }
    public static string Prompt => Read("generation-prompt.md");
    public static string Template => Read("test-suite.jsee");
}
