using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Operations;

[Description("A service finished its end-of-day work for the date; Operations opens the next day when all have replied.")]
public sealed record DayCompleted(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("The business date whose end-of-day work is complete.")] DateOnly Date,
    [property: Description("Name of the service that completed, in lower case, for example accounts.")][property: RegularExpression("^[a-z]+$")] string Service)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/day_completed/jsonschema/1-0-0";
}
