namespace Bank.Contracts.Operations;

/// <summary>A currency was activated in Operations; services keep a local copy.</summary>
public sealed record CurrencyActivated(Guid EventId, DateOnly OccurredOn, string PerformedBy, string Code, int MinorUnits) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/currency_activated/jsonschema/1-0-0";
}
