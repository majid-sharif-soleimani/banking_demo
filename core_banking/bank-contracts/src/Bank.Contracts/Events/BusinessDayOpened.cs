using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Operations opened a new business date.")]
public sealed record BusinessDayOpened(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("The business date that is now open.")] DateOnly Date) : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/business_day_opened/jsonschema/1-0-0";
}
