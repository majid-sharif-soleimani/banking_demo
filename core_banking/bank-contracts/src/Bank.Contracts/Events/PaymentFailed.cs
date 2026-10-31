using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Accounts could not collect a loan payment; the installment stays unpaid, Customers emails the customer.")]
public sealed record PaymentFailed(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("The collection this answers.")] Guid CollectionId,
    [property: Description("Borrower.")] Guid CustomerId,
    [property: Description("Installment number; null for a payoff.")] int? ScheduleItemNumber,
    [property: Description("Installment or Payoff.")][property: RegularExpression("^(Installment|Payoff)$")] string Purpose,
    [property: Description("Amount that was requested.")] Money Amount,
    [property: Description("Why, for example insufficient_funds.")][property: RegularExpression("^[a-z_]+$")] string Reason)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/payment_failed/jsonschema/1-0-0";
}
