using System.Reflection;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp;

/// <summary>
/// One tool set per role (least privilege, design plan section 6). Every tool class is registered once; each request
/// then keeps only the tools of the path it came in on. A customer's agent never even sees the admin tools' names.
/// The bank still checks every call: this is the first line, not the only one.
/// </summary>
public static class ToolSets
{
    public const string CustomerPath = "/mcp/customer";

    public const string AdminPath = "/mcp/admin";

    private static readonly HashSet<string> Customer = NamesOf(typeof(SharedTools), typeof(CustomerTools), typeof(CustomerChangeTools));

    private static readonly HashSet<string> Admin = NamesOf(typeof(SharedTools), typeof(AdminTools), typeof(AdminChangeTools));

    /// <summary>Called for every MCP request (stateless mode): removes the tools that do not belong to the request's path.</summary>
    public static Task KeepToolsOfPath(HttpContext http, McpServerOptions options, CancellationToken ct)
    {
        var allowed = http.Request.Path.StartsWithSegments(AdminPath) ? Admin : Customer;
        foreach (var tool in options.ToolCollection?.ToList() ?? [])
        {
            if (!allowed.Contains(tool.ProtocolTool.Name))
            {
                options.ToolCollection!.Remove(tool);
            }
        }

        return Task.CompletedTask;
    }

    private static HashSet<string> NamesOf(params Type[] types) =>
        [.. types.SelectMany(t => t.GetMethods()).Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>()];
}
