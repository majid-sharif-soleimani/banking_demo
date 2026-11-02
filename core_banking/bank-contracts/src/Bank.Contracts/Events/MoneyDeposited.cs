using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Cash was deposited into an account; the ledger posts the cash entry.")]
public sealed record MoneyDeposited(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the account.")] Guid AccountId,
    [property: Description("Id of the account entry; unique per deposit.")] Guid EntryId,
    [property: Description("The amount deposited, in the account currency.")] Money Amount,
    [property: Description("The account balance after the deposit.")] Money BalanceAfter)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/money_deposited/jsonschema/1-0-0";
}
