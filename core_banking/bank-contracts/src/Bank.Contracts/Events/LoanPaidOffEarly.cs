using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("A loan was paid off early; Customers sends a confirmation email.")]
public sealed record LoanPaidOffEarly(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Borrower.")] Guid CustomerId,
    [property: Description("Amount paid.")] Money Amount,
    [property: Description("Future interest the bank gave up.")] Money WaivedInterest)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_paid_off_early/jsonschema/1-0-0";
}
