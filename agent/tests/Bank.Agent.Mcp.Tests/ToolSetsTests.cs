using System.Reflection;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp.Tests;

public class ToolSetsTests
{
    private static readonly string[] AllToolNames =
    [
        .. typeof(ToolSets).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>(),
    ];

    [Fact]
    public void The_host_only_tools_are_exactly_the_upload_and_the_raw_document()
    {
        // The host keeps every tool with the prefix away from the model. A tool that carries a file's bytes must have it;
        // a tool the model should get must not.
        AllToolNames.Where(n => n.StartsWith(ToolSets.HostOnlyPrefix, StringComparison.Ordinal)).Order()
            .ShouldBe([AdminTools.CustomerDocument, CustomerChangeTools.DocumentUpload]);
    }
}
