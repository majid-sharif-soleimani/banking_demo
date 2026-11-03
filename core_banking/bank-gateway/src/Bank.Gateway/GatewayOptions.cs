namespace Bank.Gateway;

public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>
    /// The services behind the gateway, each at /api/&lt;service&gt;/…, resolved by Aspire service discovery
    /// (services:&lt;name&gt;:http:0). Starts empty: configuration arrays merge by index (stage 2 gotcha).
    /// </summary>
    public string[] Services { get; set; } = [];

    /// <summary>
    /// The shared key every /api caller sends in X-Api-Key. A secret: set it as Gateway__ApiKey (deploy/env/gateway.env
    /// at home, the stack secret on AWS), never in appsettings.json. Only Development (Aspire, tests) may leave it empty.
    /// </summary>
    public string ApiKey { get; set; } = "";
}
