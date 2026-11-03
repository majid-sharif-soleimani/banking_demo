using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.AgUi;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Features.Chats;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.Section))
    .Validate(o => o.IdleTimeout > TimeSpan.Zero, "Agent:IdleTimeout must be positive, such as \"00:05:00\".")
    .Validate(o => o.SweepInterval > TimeSpan.Zero, "Agent:SweepInterval must be positive, such as \"00:01:00\".")
    // The gateway refuses callers without its key outside Development (bank-gateway CLAUDE.md), so the host needs it too.
    .Validate(o => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.Gateway.ApiKey),
        "Agent:Gateway:ApiKey is required outside Development. Set Agent__Gateway__ApiKey (env/agent.env at home, user secrets locally).")
    .ValidateOnStart();

builder.Services.AddNpgsqlDbContext<AgentDbContext>(
    builder.Configuration.GetConnectionString("agent-db")
    ?? throw new InvalidOperationException("ConnectionStrings:agent-db is not set; the AppHost sets it."));
builder.Services.AddMigrationsOnStartup<AgentDbContext>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HostSessions>();
builder.Services.AddScoped<ChatStore>();
builder.Services.AddScoped<SessionGuard>();
builder.Services.AddHostedService<SessionSweeper>();
// "gateway" is the Aspire resource name; service discovery (services:gateway:http:0) gives the real address.
builder.Services.AddHttpClient<BankGateway>((sp, client) =>
{
    client.BaseAddress = new Uri("http://gateway");
    client.Timeout = TimeSpan.FromSeconds(10);
    var key = builder.Configuration[$"{AgentOptions.Section}:Gateway:ApiKey"];
    if (!string.IsNullOrWhiteSpace(key))
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
    }
});
// The language model behind IChatClient: Claude with a key, the echo of stage 1 without one (Features/Agent/LanguageModel.cs).
builder.AddLanguageModel();
// The bank tools come from the bank MCP server ("bank-mcp" is its Aspire resource name).
builder.Services.AddHttpClient(McpBankTools.HttpClientName, client =>
{
    client.BaseAddress = new Uri("http://bank-mcp");
    // A long statement can take a while; the gateway's own timeout is 10 seconds per bank call.
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IBankTools, McpBankTools>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (http, next) =>
{
    // The page loads only its own scripts and styles and cannot be framed by another site.
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";
    await next(http);
});
app.UseChatClientHeader();
app.MapOpenApi();
app.MapDefaultEndpoints();

AuthEndpoints.Map(app);
AgUiEndpoint.Map(app);
ActionEndpoints.Map(app);
ChatEndpoints.Map(app);

// The chat UI (agent/chat-ui, built into wwwroot): same origin as the API, so the cookie needs no CORS.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
