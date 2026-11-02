using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Lending disbursed a loan; Accounts credits the repayment account and Ledger posts the loan.")]
public sealed record LoanDisbursed(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Id of the application.")] Guid ApplicationId,
    [property: Description("Borrower.")] Guid CustomerId,
    [property: Description("Account credited now and debited for installments.")] Guid RepaymentAccountId,
    [property: Description("Amount disbursed.")] Money Principal)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_disbursed/jsonschema/1-0-0";
}
