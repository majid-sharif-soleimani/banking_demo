using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("The customer asked to pay off a loan early; Accounts debits the quoted amount and answers.")]
public sealed record LoanPayoffRequested(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Unique id of this payoff attempt.")] Guid CollectionId,
    [property: Description("Repayment account.")] Guid AccountId,
    [property: Description("The payoff quote for the business date.")] Money Amount)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_payoff_requested/jsonschema/1-0-0";
}
