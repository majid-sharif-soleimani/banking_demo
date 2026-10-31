using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("The reviewer needs more information; Customers creates an information request and emails the customer.")]
public sealed record InformationRequested(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the request, chosen by Lending.")] Guid InformationRequestId,
    [property: Description("Id of the application.")] Guid ApplicationId,
    [property: Description("Applicant.")] Guid CustomerId,
    [property: Description("What is needed, one item per line.")] string RequestedItems)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/information_requested/jsonschema/1-0-0";
}
