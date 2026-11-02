using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Cash was withdrawn from an account; the ledger posts the cash entry.")]
public sealed record MoneyWithdrawn(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the account.")] Guid AccountId,
    [property: Description("Id of the account entry; unique per withdrawal.")] Guid EntryId,
    [property: Description("The amount withdrawn, in the account currency, positive.")] Money Amount,
    [property: Description("The account balance after the withdrawal.")] Money BalanceAfter)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/money_withdrawn/jsonschema/1-0-0";
}
