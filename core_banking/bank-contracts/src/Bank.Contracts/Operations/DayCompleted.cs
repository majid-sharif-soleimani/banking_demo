namespace Bank.Contracts.Operations;

/// <summary>A service finished its end-of-day work for the date.</summary>
public sealed record DayCompleted(Guid EventId, DateOnly OccurredOn, string PerformedBy, DateOnly Date, string Service) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/day_completed/jsonschema/1-0-0";
}
