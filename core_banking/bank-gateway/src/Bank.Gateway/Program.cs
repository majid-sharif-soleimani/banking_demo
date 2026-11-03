using Bank.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var options = builder.Configuration.GetSection(GatewayOptions.Section).Get<GatewayOptions>() ?? new GatewayOptions();
// Configuration arrays merge by index across sources: a blank entry switches a service off (Gateway__Services__3=""),
// and a name listed twice counts once.
options.Services = [.. options.Services.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()];
if (options.Services.Length == 0)
{
    throw new InvalidOperationException("Gateway:Services must list the services behind the gateway, such as [\"accounts\", \"lending\"].");
}

// Every /api call needs a session from the identity service (workspace ADR 0007), so it must be behind the gateway too.
if (!options.Services.Contains(GatewayMiddleware.Identity))
{
    throw new InvalidOperationException($"Gateway:Services must include \"{GatewayMiddleware.Identity}\": it logs callers in and checks their tokens.");
}

if (string.IsNullOrWhiteSpace(options.ApiKey) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Gateway:ApiKey is not set. Set Gateway__ApiKey in deploy/env/gateway.env (home-hosting.md, step 3) or GATEWAY_API_KEY in the stack secret on AWS.");
}

var (routes, clusters) = Routes.For(options.Services);
builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddHttpClient("downstream", client => client.Timeout = TimeSpan.FromSeconds(5));
// The token check on every /api request: http://identity is resolved by service discovery like the YARP routes.
builder.Services.AddHttpClient(GatewayMiddleware.Identity, client =>
{
    client.BaseAddress = new Uri($"http://{GatewayMiddleware.Identity}");
    client.Timeout = TimeSpan.FromSeconds(5);
});
var health = builder.Services.AddHealthChecks();
foreach (var service in options.Services)
{
    health.Add(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(
        $"service:{service}", sp => new DownstreamHealthCheck(sp.GetRequiredService<IHttpClientFactory>(), service), null, ["ready"]));
}

var app = builder.Build();

app.UseBankHeaders(options.ApiKey);
app.MapDefaultEndpoints();

// Aggregated OpenAPI: where each service's document is, through the gateway.
app.MapGet("/openapi", () => Results.Ok(options.Services.Select(s => new { service = s, url = $"/api/{s}/openapi/v1.json" })));
app.MapReverseProxy();

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
