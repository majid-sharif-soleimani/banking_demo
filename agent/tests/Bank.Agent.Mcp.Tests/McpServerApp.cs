using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Bank.Agent.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Bank.Agent.Mcp.Tests;

/// <summary>
/// The real MCP server (its Program.cs and tools) with <see cref="FakeGateway"/> in place of the gateway. A test talks
/// to it the way the agent host does: an MCP client over HTTP, with the bank token and the customer id as headers.
/// </summary>
public sealed class McpServerApp : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-gateway-key";

    public FakeGateway Gateway { get; } = new();

    /// <summary>The admin's connection: the admin's bank token and no customer id, on the admin path.</summary>
    public Task<McpClient> ConnectAdminAsync(string token, string? idempotencyKey = null) =>
        ConnectAsync(token, null, idempotencyKey, ToolSets.AdminPath);

    public async Task<McpClient> ConnectAsync(string? token, Guid? customerId, string? idempotencyKey = null, string path = ToolSets.CustomerPath)
    {
        var headers = new Dictionary<string, string>();
        if (idempotencyKey is not null)
        {
            headers[BankApi.IdempotencyKeyHeader] = idempotencyKey;
        }

        if (token is not null)
        {
            headers["Authorization"] = $"Bearer {token}";
        }

        if (customerId is not null)
        {
            headers[BankApi.CustomerIdHeader] = customerId.Value.ToString();
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(Server.BaseAddress, path), AdditionalHeaders = headers },
            CreateClient(), ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("BankMcp:Gateway:ApiKey", ApiKey);
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
            services.AddHttpClient<BankApi>().ConfigurePrimaryHttpMessageHandler(() => Gateway));
    }
}

/// <summary>Answers every gateway call with a fixed status and body, and records what it was asked.</summary>
public sealed class FakeGateway : HttpMessageHandler
{
    public ConcurrentQueue<(string PathAndQuery, string? Authorization, string? ApiKey)> Requests { get; } = new();

    /// <summary>Method, idempotency key and body of every call, for the change tools.</summary>
    public ConcurrentQueue<(HttpMethod Method, string? IdempotencyKey, string? Body)> Sent { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string Body { get; set; } = """{"ok":true}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Sent.Enqueue((request.Method,
            request.Headers.TryGetValues("Idempotency-Key", out var idempotency) ? idempotency.Single() : null,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        Requests.Enqueue((
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null));
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}
