using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("A test card payment for a loan was declined; Customers emails the customer.")]
public sealed record CardPaymentDeclined(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the card payment.")] Guid PaymentId,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Borrower.")] Guid CustomerId,
    [property: Description("Amount that was attempted.")] Money Amount,
    [property: Description("declined or insufficient_funds.")][property: RegularExpression("^[a-z_]+$")] string Reason)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/card_payment_declined/jsonschema/1-0-0";
}
