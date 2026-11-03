using Bank.Agent.Host.Features.Auth;
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

    /// <summary>A change or host-only tool by name, for the host to run after the person's action.</summary>
    public AIFunction? Find(string name) => Changes.Concat(HostOnly).FirstOrDefault(t => t.Name == name);

    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Where the agent's bank tools come from. An interface so the integration tests can give the agent fake bank tools.</summary>
public interface IBankTools
{
    /// <summary>
    /// Opens the customer's bank tools, acting for the session's customer with its bank token. With
    /// <paramref name="confirmationId"/> (only after the person confirmed), the change tools may run; the id becomes the
    /// bank's idempotency key, so a confirmation sent twice makes one change.
    /// </summary>
    Task<BankToolSet> OpenForCustomerAsync(HostSession session, CancellationToken ct, string? confirmationId = null);
}

/// <summary>
/// The bank tools from the bank MCP server (agent/src/Bank.Agent.Mcp, design plan section 3.3).
///
/// How the token stays out of the model: the token and the customer id go on the MCP connection as HTTP headers, set
/// here from the host session. The model only sees each tool's name, description and parameters, and chooses the
/// arguments (an account id, two dates). It never sees the headers, so it cannot read, repeat or change them.
///
/// One connection per run keeps it simple: the headers are those of this session, and nothing outlives the run.
/// </summary>
public sealed class McpBankTools(IHttpClientFactory httpClients, ILoggerFactory loggers) : IBankTools
{
    public const string HttpClientName = "bank-mcp";

    /// <summary>Tools of the MCP server that only the host calls (Bank.Agent.Mcp, CustomerChangeTools.DocumentUpload).</summary>
    public static readonly string[] HostOnlyTools = ["document_upload"];

    public async Task<BankToolSet> OpenForCustomerAsync(HostSession session, CancellationToken ct, string? confirmationId = null)
    {
        var http = httpClients.CreateClient(HttpClientName);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {session.BankToken}",
            ["X-Customer-Id"] = session.Principal.CustomerId?.ToString() ?? "",
        };
        if (confirmationId is not null)
        {
            headers["Idempotency-Key"] = confirmationId;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp/customer"), AdditionalHeaders = headers },
            http, loggers, ownsHttpClient: true);
        var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
        try
        {
            // Each McpClientTool is an AIFunction: the agent can call it like any local function, and the call goes
            // over MCP to the server.
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            // The MCP server marks every tool that changes data with ReadOnly = false (its "readOnlyHint" annotation).
            var hostOnly = tools.Where(t => HostOnlyTools.Contains(t.Name)).ToList();
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
