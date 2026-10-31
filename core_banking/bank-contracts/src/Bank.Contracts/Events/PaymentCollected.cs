using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Accounts debited a loan payment; Lending allocates it.")]
public sealed record PaymentCollected(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("The collection this answers.")] Guid CollectionId,
    [property: Description("Installment number; null for a payoff.")] int? ScheduleItemNumber,
    [property: Description("Installment or Payoff.")][property: RegularExpression("^(Installment|Payoff)$")] string Purpose,
    [property: Description("Amount debited.")] Money Amount)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/payment_collected/jsonschema/1-0-0";
}
