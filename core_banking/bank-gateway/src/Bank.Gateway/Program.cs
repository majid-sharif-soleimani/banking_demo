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

var (routes, clusters) = Routes.For(options.Services);
builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddHttpClient("downstream", client => client.Timeout = TimeSpan.FromSeconds(5));
var health = builder.Services.AddHealthChecks();
foreach (var service in options.Services)
{
    health.Add(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(
        $"service:{service}", sp => new DownstreamHealthCheck(sp.GetRequiredService<IHttpClientFactory>(), service), null, ["ready"]));
}

var app = builder.Build();

app.UseBankHeaders();
app.MapDefaultEndpoints();

// Aggregated OpenAPI: where each service's document is, through the gateway.
app.MapGet("/openapi", () => Results.Ok(options.Services.Select(s => new { service = s, url = $"/api/{s}/openapi/v1.json" })));
app.MapReverseProxy();

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
