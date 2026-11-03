using Bank.Agent.Host.Features.Auth;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// The bank tools of one run, and the MCP connection they live on. Disposing it closes the connection.
/// </summary>
public sealed class BankToolSet(IReadOnlyList<AITool> tools, IAsyncDisposable? connection = null) : IAsyncDisposable
{
    public static BankToolSet None => new([]);

    public IReadOnlyList<AITool> Tools { get; } = tools;

    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Where the agent's bank tools come from. An interface so the integration tests can give the agent fake bank tools.</summary>
public interface IBankTools
{
    /// <summary>Opens the customer's bank tools for one run, acting for the session's customer with its bank token.</summary>
    Task<BankToolSet> OpenForCustomerAsync(HostSession session, CancellationToken ct);
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

    public async Task<BankToolSet> OpenForCustomerAsync(HostSession session, CancellationToken ct)
    {
        var http = httpClients.CreateClient(HttpClientName);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp/customer"),
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {session.BankToken}",
                    ["X-Customer-Id"] = session.Principal.CustomerId?.ToString() ?? "",
                },
            },
            http, loggers, ownsHttpClient: true);
        var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
        try
        {
            // Each McpClientTool is an AIFunction: the agent can call it like any local function, and the call goes
            // over MCP to the server.
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            return new BankToolSet([.. tools], client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}
