using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("An application moved to another status; Customers records it in the interaction history.")]
public sealed record LoanApplicationStatusChanged(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the application.")] Guid ApplicationId,
    [property: Description("Applicant.")] Guid CustomerId,
    [property: Description("Previous status.")][property: RegularExpression("^[A-Za-z]+$")] string FromStatus,
    [property: Description("New status.")][property: RegularExpression("^[A-Za-z]+$")] string ToStatus,
    [property: Description("Why, for a rejection or cancellation; otherwise null.")] string? Reason)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_application_status_changed/jsonschema/1-0-0";
}
