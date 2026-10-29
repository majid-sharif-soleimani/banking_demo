using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.Contracts;

/// <summary>
/// The one set of JSON options for event payloads: camelCase names, ISO dates, numbers only as JSON numbers.
/// Publishers and consumers must both use it, or names and formats drift away from the schemas.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
