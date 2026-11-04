using System.Text.Json;

namespace Bank.Agent.Host.Features;

/// <summary>The host's JSON settings (camelCase, web defaults) for what it stores, shows and sends to the bank tools.</summary>
public static class HostJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
