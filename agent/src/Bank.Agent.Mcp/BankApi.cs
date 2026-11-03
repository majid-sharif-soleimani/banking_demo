using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The customer the MCP connection acts for, as the agent host sent it.</summary>
    public Guid CustomerId =>
        Guid.TryParse(Incoming().Headers[CustomerIdHeader].ToString(), out var id)
            ? id
            : throw new McpException("This tool needs a logged-in customer.");

    /// <summary>
    /// GETs a bank path through the gateway and returns the bank's JSON as it is. The model reads the JSON; the bank's
    /// numbers reach it unchanged (design plan section 6: "the bank decides, the agent asks").
    /// </summary>
    public Task<string> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    /// <summary>
    /// A read that the bank serves as a POST because its input is a body (a loan simulation). It changes nothing.
    /// </summary>
    public Task<string> PostReadAsync(string path, object body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body, ct);

    /// <summary>
    /// A change: a POST or DELETE that moves money or books something. It runs only with an Idempotency-Key on the MCP
    /// connection. The agent host sets that key only when the person has pressed Confirm (design plan flow 4.3), using
    /// the confirmation's id, so a change the model asks for directly is refused here as well, and a confirmation that
    /// is sent twice makes one change.
    /// </summary>
    public Task<string> ChangeAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var key = Incoming().Headers[IdempotencyKeyHeader].ToString();
        return string.IsNullOrWhiteSpace(key)
            ? throw new McpException("A change needs the person's confirmation. The agent host asks for it; do not call change tools directly.")
            : SendAsync(method, path, body, ct, key);
    }

    /// <summary>A document the person uploaded in the chat, sent to Customers as a multipart form. A change like the others.</summary>
    public Task<string> UploadAsync(string path, Guid applicationId, string fileName, string contentType, byte[] content, CancellationToken ct)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Upload"), "kind" },
            { new StringContent(applicationId.ToString()), "applicationId" },
        };
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);
        return ChangeAsync(HttpMethod.Post, path, form, ct);
    }

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, string? idempotencyKey = null)
    {
        var authorization = Incoming().Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            throw new McpException("No bank session came with this call. Ask the person to log in again.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        }

        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, options: Json),
        };

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrEmpty(text) ? """{"ok":true}""" : text;
        }

        // A tool error is not a crash: McpException's message goes back to the model as the tool's result, so it can
        // explain the problem to the person instead of inventing an answer.
        throw response.StatusCode == HttpStatusCode.Unauthorized
            ? new McpException("The bank session has ended. Tell the person to log in again.")
            : new McpException($"The bank refused the call ({(int)response.StatusCode}): {text}");
    }

    private HttpRequest Incoming() =>
        context.HttpContext?.Request ?? throw new InvalidOperationException("A bank call outside an MCP request.");
}
