using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// The bank tools of one run, and the MCP connection they live on. Disposing it closes the connection.
///
/// Three groups: reads the model may call freely, changes the model may only ask for (they need the person's
/// confirmation, see <see cref="ConfirmFirst"/>), and host-only tools the model never gets (the document upload, which
/// carries the file the person chose).
/// </summary>
public sealed class BankToolSet(
    IReadOnlyList<AIFunction> reads, IReadOnlyList<AIFunction> changes, IReadOnlyList<AIFunction> hostOnly, IAsyncDisposable? connection = null)
    : IAsyncDisposable
{
    public static BankToolSet None => new([], [], []);

    public IReadOnlyList<AIFunction> Reads { get; } = reads;

    public IReadOnlyList<AIFunction> Changes { get; } = changes;

    public IReadOnlyList<AIFunction> HostOnly { get; } = hostOnly;

    /// <summary>A change or host-only tool by name, for the host to run itself.</summary>
    public AIFunction? Find(string name) => Changes.Concat(HostOnly).FirstOrDefault(t => t.Name == name);

    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Where the agent's bank tools come from. An interface so the integration tests can give the agent fake bank tools.</summary>
public interface IBankTools
{
    /// <summary>
    /// Opens the bank tools of the session's role (the customer's or the admin's set), acting with the session's bank
    /// token. With <paramref name="confirmationId"/> (only after the person confirmed), the change tools may run; the id
    /// becomes the bank's idempotency key, so a confirmation sent twice makes one change.
    /// </summary>
    Task<BankToolSet> OpenAsync(HostSession session, CancellationToken ct, string? confirmationId = null);
}

/// <summary>
/// The bank tools from the bank MCP server (agent/src/Bank.Agent.Mcp, design plan section 3.3).
///
/// How the token stays out of the model: the token and the customer id go on the MCP connection as HTTP headers, set
/// here from the host session. The model only sees each tool's name, description and parameters, and chooses the
/// arguments (an account id, two dates). It never sees the headers, so it cannot read, repeat or change them.
///
/// One connection per run keeps it simple: the headers are those of this session, and nothing outlives the run.
///
/// Each role has its own path on the MCP server, and the server offers only that role's tools there: /mcp/customer
/// for a customer (with X-Customer-Id), /mcp/admin for the admin (no customer id; the admin's tools take one as a
/// parameter). Which path is chosen comes from the session, never from the model.
/// </summary>
public sealed class McpBankTools(IHttpClientFactory httpClients, ILoggerFactory loggers) : IBankTools
{
    public const string HttpClientName = "bank-mcp";

    /// <summary>
    /// The MCP server names a tool only the host may call with this prefix (Bank.Agent.Mcp ToolSets.HostOnlyPrefix). The
    /// host keeps every such tool away from the model, whatever its name, so a new or renamed one cannot reach the model.
    /// </summary>
    public const string HostOnlyPrefix = "host_";

    /// <summary>The upload the host runs when the person sends a file (CustomerChangeTools.DocumentUpload).</summary>
    public const string DocumentUpload = HostOnlyPrefix + "document_upload";

    /// <summary>The raw document, read only through <see cref="DocumentReader"/> (AdminTools.CustomerDocument).</summary>
    public const string CustomerDocument = HostOnlyPrefix + "customer_document";

    public async Task<BankToolSet> OpenAsync(HostSession session, CancellationToken ct, string? confirmationId = null)
    {
        var http = httpClients.CreateClient(HttpClientName);
        var isAdmin = session.Principal.Role == Role.Admin;
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {session.BankToken}" };
        if (!isAdmin)
        {
            headers["X-Customer-Id"] = session.Principal.CustomerId?.ToString() ?? "";
        }

        if (confirmationId is not null)
        {
            headers["Idempotency-Key"] = confirmationId;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, isAdmin ? "/mcp/admin" : "/mcp/customer"), AdditionalHeaders = headers },
            http, loggers, ownsHttpClient: true);
        var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
        try
        {
            // Each McpClientTool is an AIFunction: the agent can call it like any local function, and the call goes
            // over MCP to the server.
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            // The MCP server marks every tool that changes data with ReadOnly = false (its "readOnlyHint" annotation).
            var hostOnly = tools.Where(t => t.Name.StartsWith(HostOnlyPrefix, StringComparison.Ordinal)).ToList();
            var rest = tools.Except(hostOnly).ToList();
            return new BankToolSet(
                [.. rest.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true)],
                [.. rest.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint != true)],
                [.. hostOnly],
                client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}
