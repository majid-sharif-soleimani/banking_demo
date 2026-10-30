using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Operations closed the business date; Accounts and Lending run their end-of-day work for it.")]
public sealed record DayClosed(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("The business date that was closed.")] DateOnly Date) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/day_closed/jsonschema/1-0-0";
}
