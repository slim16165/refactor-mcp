using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Linq;
using RefactorMCP.ConsoleApp.Infrastructure;

namespace RefactorMCP.ConsoleApp.Tools;

[McpServerToolType]
public static class ListTools
{
    [McpServerTool, Description("List all available refactoring tools")]
    public static string ListToolsCommand()
    {
        return string.Join('\n', ToolRegistry.GetAvailableToolNames());
    }
}
