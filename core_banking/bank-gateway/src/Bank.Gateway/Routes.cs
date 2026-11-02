using Yarp.ReverseProxy.Configuration;

namespace Bank.Gateway;

/// <summary>One YARP route and cluster per service: /api/{service}/{**rest} → http://{service}/{rest} (design plan ADR 12).</summary>
public static class Routes
{
    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) For(IEnumerable<string> services)
    {
        var list = services.ToList();
        return (
            [.. list.Select(service => new RouteConfig
            {
                RouteId = service,
                ClusterId = service,
                Match = new RouteMatch { Path = $"/api/{service}/{{**rest}}" },
                Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = $"/api/{service}" }],
            })],
            [.. list.Select(service => new ClusterConfig
            {
                ClusterId = service,
                // The host name is the Aspire resource name; service discovery turns it into the real address.
                Destinations = new Dictionary<string, DestinationConfig> { [service] = new() { Address = $"http://{service}" } },
            })]);
    }
}
