using System.Reflection;
using E2ETester.Application;
using E2ETester.Infrastructure;
using E2ETester.Mcp;
using ModelContextProtocol.Server;

try
{
    if (args.Length != 2 || args[0] != "--config") throw new InvalidDataException();
    var path = Path.GetFullPath(args[1]);
    if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException();
    var configuration = PackageJson.Read<AgentConfiguration>(await File.ReadAllTextAsync(path));
    await using var workspace = new AgentWorkspace(configuration);
    var toolHost = new AgentTools(workspace);
    var options = new McpServerOptions { ServerInfo = new() { Name = "E2E Tester", Version = "0.7.5" } };
    options.ToolCollection = new();
    foreach (var method in typeof(AgentTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        options.ToolCollection.Add(McpServerTool.Create(method, toolHost));
    await using var server = McpServer.Create(new StdioServerTransport("E2E Tester"), options);
    await server.RunAsync();
}
catch
{
    await Console.Error.WriteLineAsync("E2E Tester MCP could not start or complete. Use --config <local configuration.json>; check workspace ownership, permissions and configuration. No credentials are logged.");
    Environment.ExitCode = 1;
}