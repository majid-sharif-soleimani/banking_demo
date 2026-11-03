using System.Net;
using System.Net.Http.Headers;
using ModelContextProtocol;

namespace Bank.Agent.Mcp;

public sealed class BankMcpOptions
{
    public const string Section = "BankMcp";

    public GatewayOptions Gateway { get; set; } = new();

    public sealed class GatewayOptions
    {
        /// <summary>The gateway's shared key (X-Api-Key). A secret: user secrets locally, env/bank-mcp.env at home.</summary>
        public string? ApiKey { get; set; }
    }
}

/// <summary>
/// The MCP server's only way into the bank: the gateway (phase 2 design plan, section 3.3).
///
/// Who is calling? The agent host opens an MCP connection per chat run and puts two headers on it: the bank token of
/// the logged-in person (<c>Authorization: Bearer ...</c>) and their customer id (<c>X-Customer-Id</c>). Neither comes
/// from the language model, so the model can neither see the token nor pick another customer. This class copies the
/// token onto every gateway call; the gateway and the bank services still decide what that person may see.
/// </summary>
public sealed class BankApi(HttpClient http, IHttpContextAccessor context)
{
    public const string CustomerIdHeader = "X-Customer-Id";

    /// <summary>The customer the MCP connection acts for, as the agent host sent it.</summary>
    public Guid CustomerId =>
        Guid.TryParse(Incoming().Headers[CustomerIdHeader].ToString(), out var id)
            ? id
            : throw new McpException("This tool needs a logged-in customer.");

    /// <summary>
    /// GETs a bank path through the gateway and returns the bank's JSON as it is. The model reads the JSON; the bank's
    /// numbers reach it unchanged (design plan section 6: "the bank decides, the agent asks").
    /// </summary>
    public async Task<string> GetAsync(string path, CancellationToken ct)
    {
        var authorization = Incoming().Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            throw new McpException("No bank session came with this call. Ask the person to log in again.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        // A tool error is not a crash: McpException's message goes back to the model as the tool's result, so it can
        // explain the problem to the person instead of inventing an answer.
        throw response.StatusCode == HttpStatusCode.Unauthorized
            ? new McpException("The bank session has ended. Tell the person to log in again.")
            : new McpException($"The bank refused the call ({(int)response.StatusCode}): {body}");
    }

    private HttpRequest Incoming() =>
        context.HttpContext?.Request ?? throw new InvalidOperationException("A bank call outside an MCP request.");
}
