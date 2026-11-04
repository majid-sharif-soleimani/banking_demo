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

    /// <summary>
    /// The name prefix of a tool only the agent host may call (an upload's bytes, the raw document). The host keeps
    /// every tool with this prefix away from the model, so the server says which tools are host-only, in one place,
    /// and a new or renamed host-only tool cannot reach the model by mistake. Same value as McpBankTools.HostOnlyPrefix.
    /// </summary>
    public const string HostOnlyPrefix = "host_";

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
