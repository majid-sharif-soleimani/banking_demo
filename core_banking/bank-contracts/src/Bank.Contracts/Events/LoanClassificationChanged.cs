using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("A loan moved between Current, Overdue and Default; Customers sends a warning email.")]
public sealed record LoanClassificationChanged(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Borrower.")] Guid CustomerId,
    [property: Description("Previous classification.")][property: RegularExpression("^(Current|Overdue|Default)$")] string FromClassification,
    [property: Description("New classification.")][property: RegularExpression("^(Current|Overdue|Default)$")] string ToClassification,
    [property: Description("Days past due on the business date.")] int DaysPastDue)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_classification_changed/jsonschema/1-0-0";
}
