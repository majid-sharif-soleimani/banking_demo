using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.Contracts;

/// <summary>
/// The one set of JSON options for event payloads: camelCase names, ISO dates, numbers only as JSON numbers.
/// The event JSON Schemas are generated from the records with these options, so publishers and consumers
/// must both use them, or the wire format drifts away from the schemas.
/// Reading rejects what the schemas reject: a missing required field, or null in a non-nullable one.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
