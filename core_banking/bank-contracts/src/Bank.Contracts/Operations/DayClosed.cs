namespace Bank.Contracts.Operations;

/// <summary>Operations closed the business date; Accounts and Lending run their end-of-day work for it.</summary>
public sealed record DayClosed(Guid EventId, DateOnly OccurredOn, string PerformedBy, DateOnly Date) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/day_closed/jsonschema/1-0-0";
}
