using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("A test card payment for a loan was captured; Lending allocates it like an installment payment.")]
public sealed record CardPaymentCaptured(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the card payment.")] Guid PaymentId,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Installment the customer chose; null for the overdue amount.")] int? ScheduleItemNumber,
    [property: Description("Amount captured.")] Money Amount)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/card_payment_captured/jsonschema/1-0-0";
}
