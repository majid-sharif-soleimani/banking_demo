using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("A customer applied for a loan; Customers records it in the interaction history.")]
public sealed record LoanApplicationSubmitted(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the application.")] Guid ApplicationId,
    [property: Description("Applicant.")] Guid CustomerId,
    [property: Description("Loan product code, for example CAR.")][property: RegularExpression("^[A-Z]+$")] string ProductCode,
    [property: Description("Amount applied for.")] Money RequestedAmount,
    [property: Description("Term in months.")] int TermMonths)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_application_submitted/jsonschema/1-0-0";
}
