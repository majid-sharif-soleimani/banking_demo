using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Operations activated a currency; every service keeps a local copy of active currencies.")]
public sealed record CurrencyActivated(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("ISO 4217 code, for example SEK.")][property: RegularExpression("^[A-Z]{3}$")] string Code,
    [property: Description("Digits after the decimal point, for example 2 for SEK; at most 28, the most a decimal holds.")][property: Range(0, 28)] int MinorUnits)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/currency_activated/jsonschema/1-0-0";
}
