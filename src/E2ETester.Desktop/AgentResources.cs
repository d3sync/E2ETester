using System.IO;
namespace E2ETester.Desktop;
internal static class AgentResources
{
    private static string Read(string name) { using var reader=new StreamReader(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Agent/"+name)).Stream); return reader.ReadToEnd(); }
    public static string Prompt => Read("generation-prompt.md");
    public static string Template => Read("test-suite.jsee");
    private static readonly string[] SkillFiles = ["SKILL.md", "scripts/Setup-Agent.ps1", "references/test-package.v1.4.schema.json", "references/test-package.v1.5.schema.json", "references/preparation.md", "assets/prepared-permissions.jsee", "references/mcp-setup.md", "references/generation-prompt.md", "assets/test-suite.jsee", "agents/openai.yaml"];
    internal static string SkillText(string name)
    {
        using var reader=new StreamReader(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/AgentSkill/"+name)).Stream);
        return reader.ReadToEnd();
    }
    internal static void ExportKit(string directory)
    {
        foreach(var name in SkillFiles)
        {
            var target=Path.Combine(directory,"e2e-tester-author",name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,SkillText(name));
        }
        var executableDirectory=Path.GetDirectoryName(Environment.ProcessPath!)!;
        var candidates=new[]{Path.Combine(executableDirectory,"mcp","E2ETester.Mcp.exe"),Path.Combine(executableDirectory,"..","mcp","E2ETester.Mcp.exe")};
        var source=candidates.FirstOrDefault(File.Exists);
        if(source is not null)File.Copy(source,Path.Combine(directory,"E2ETester.Mcp.exe"),true);
        File.WriteAllText(Path.Combine(directory,"START-HERE.txt"),SetupText);
    }
    private const string SetupText = "Copy prompt: paste into your AI agent to generate a package.\nSkill: the full e2e-tester-author folder is exported with its schema and template.\n\nExport the AI setup kit to a new folder. In PowerShell from that folder, run:\n\n.\\e2e-tester-author\\scripts\\Setup-Agent.ps1 -McpExecutable .\\E2ETester.Mcp.exe -BaseUrl https://YOUR-API-HOST -ConfigureCodex\n\nReplace YOUR-API-HOST with your API server. The installer and full distribution include the MCP executable; if using only a standalone desktop EXE, obtain the MCP executable from the full distribution.\n\nThe setup copies the skill into your Codex skills directory and registers the MCP server. Restart your agent afterward. Other clients can use the generated mcp-client.json. No credentials are included. MCP authentication uses the E2ETESTER_CLOUD_TOKEN environment variable; it does not read desktop browser sessions.";
    internal static void ShowHelp(System.Windows.Window? owner)
    {
        var window=new TextDocumentWindow("AI assistant · prompt, skill and setup",("Copy prompt",Prompt),("Package template",Template),("Skill",SkillText("SKILL.md")),("Setup",SetupText)){Owner=owner};
        window.AddAction("Export AI setup kit…",()=>
        {
            var picker=new Microsoft.Win32.OpenFolderDialog {Title="Choose a folder for the AI setup kit"};
            if(picker.ShowDialog(window)!=true)return;
            try { var target=Path.Combine(picker.FolderName,"E2ETester-Agent-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")); ExportKit(target); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target){UseShellExecute=true}); }
            catch(Exception ex){System.Windows.MessageBox.Show(window,"Could not export the kit: "+ex.Message);}
        });
        window.ShowDialog();
    }
}
