namespace Bank.Contracts.Testing;

/// <summary>
/// Used only by the messaging integration tests (ordering by Key, duplicates, retries). No service publishes or consumes it.
/// </summary>
public sealed record TestEvent(Guid EventId, DateOnly OccurredOn, string PerformedBy, string Key, int Sequence, Money Amount)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/test_event/jsonschema/1-0-0";
}
