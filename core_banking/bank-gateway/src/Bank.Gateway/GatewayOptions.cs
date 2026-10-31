namespace Bank.Gateway;

public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>
    /// The services behind the gateway, each at /api/&lt;service&gt;/…, resolved by Aspire service discovery
    /// (services:&lt;name&gt;:http:0). Starts empty: configuration arrays merge by index (stage 2 gotcha).
    /// </summary>
    public string[] Services { get; set; } = [];
}
