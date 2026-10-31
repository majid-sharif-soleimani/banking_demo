using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bank.Gateway;

/// <summary>One downstream service's /health, through service discovery. The gateway's /health includes all of them.</summary>
public sealed class DownstreamHealthCheck(IHttpClientFactory clients, string service) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await clients.CreateClient("downstream").GetAsync($"http://{service}/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"{service} is healthy.")
                : HealthCheckResult.Unhealthy($"{service} answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{service} is unreachable.", ex);
        }
    }
}
