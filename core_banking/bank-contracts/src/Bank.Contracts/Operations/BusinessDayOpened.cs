namespace Bank.Contracts.Operations;

/// <summary>Operations opened a new business date.</summary>
public sealed record BusinessDayOpened(Guid EventId, DateOnly OccurredOn, string PerformedBy, DateOnly Date) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/business_day_opened/jsonschema/1-0-0";
}
