using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

/// <summary>Messaging integration tests use it for ordering by Key, duplicates and retries.</summary>
[Description("Used only by the messaging integration tests. No service publishes or consumes it.")]
public sealed record TestEvent(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Message key; events with the same key must be handled in order.")][property: MinLength(1)] string Key,
    [property: Description("Position of the event within its key.")][property: Range(0, int.MaxValue)] int Sequence,
    [property: Description("Money; amount is a decimal string so no consumer loses precision.")] Money Amount)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/test_event/jsonschema/1-0-0";
}
