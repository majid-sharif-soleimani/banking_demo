using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Money moved between two accounts in the bank, converted when their currencies differ; the ledger posts the FX entry.")]
public sealed record MoneyTransferred(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the transfer.")] Guid TransferId,
    [property: Description("Account debited.")] Guid FromAccountId,
    [property: Description("Account credited.")] Guid ToAccountId,
    [property: Description("Amount debited, in the source account currency.")] Money Amount,
    [property: Description("Amount credited, in the target account currency; equal to Amount when the currencies are the same.")] Money ConvertedAmount,
    [property: Description("Units of the target currency per unit of the source currency, as a decimal string; null without conversion.")][property: RegularExpression("^[0-9]+(\\.[0-9]+)?$")] string? FxRate,
    [property: Description("Where the rate came from, for example riksbank; null without conversion.")] string? FxSource,
    [property: Description("Id of the standing order that made the transfer; null for a direct transfer.")] Guid? StandingOrderId,
    [property: Description("Fee charged to the source account; always null in phase 1 (no fees), kept so a fee is not a breaking change.")] Money? Fee)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/money_transferred/jsonschema/1-0-0";
}
