using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Accounts opened an account; it can now receive money and serve as a loan repayment account.")]
public sealed record AccountOpened(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the account.")] Guid AccountId,
    [property: Description("The 9-digit account number: 8 random digits and a Luhn check digit.")][property: RegularExpression("^[1-9][0-9]{8}$")] string AccountNumber,
    [property: Description("Id of the owning customer.")] Guid CustomerId,
    [property: Description("Current or Savings.")][property: RegularExpression("^(Current|Savings)$")] string AccountType,
    [property: Description("ISO 4217 currency of the account, for example SEK.")][property: RegularExpression("^[A-Z]{3}$")] string Currency)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/account_opened/jsonschema/1-0-0";
}
