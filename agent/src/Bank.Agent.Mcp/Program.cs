using Bank.Agent.Mcp;

// The bank MCP server (phase 2 design plan, section 3.3, decision A2). MCP, the Model Context Protocol, is a standard
// way to offer tools to a language model: a server lists its tools (name, description, JSON schema of the parameters)
// and runs them when a client asks. Here the client is the agent host. The server has no state and no business rules:
// every tool is a call through the gateway, and the bank stays the security boundary.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<BankMcpOptions>()
    .Bind(builder.Configuration.GetSection(BankMcpOptions.Section))
    // The gateway refuses callers without its key outside Development (bank-gateway CLAUDE.md).
    .Validate(o => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.Gateway.ApiKey),
        "BankMcp:Gateway:ApiKey is required outside Development. Set BankMcp__Gateway__ApiKey (env/bank-mcp.env at home, user secrets locally).")
    .ValidateOnStart();

builder.Services.AddHttpContextAccessor();
// "gateway" is the Aspire resource name; service discovery gives the real address.
builder.Services.AddHttpClient<BankApi>(client =>
{
    client.BaseAddress = new Uri("http://gateway");
    client.Timeout = TimeSpan.FromSeconds(10);
    var key = builder.Configuration[$"{BankMcpOptions.Section}:Gateway:ApiKey"];
    if (!string.IsNullOrWhiteSpace(key))
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
    }
});

// Streamable HTTP transport, stateless: every MCP request stands alone, so the headers of that request (the bank token
// and the customer id) are the only context a tool has. Nothing is remembered between calls.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CustomerTools>()
    .WithTools<CustomerChangeTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
// One tool set per role (least privilege, design plan section 6). The admin set comes in stage 4 at /mcp/admin.
app.MapMcp("/mcp/customer");

app.Run();

/// <summary>Visible to WebApplicationFactory in the tests.</summary>
public partial class Program;
