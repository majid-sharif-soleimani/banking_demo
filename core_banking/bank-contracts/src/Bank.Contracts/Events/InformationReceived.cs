using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("The customer delivered the requested information; Lending returns the application to UnderReview.")]
public sealed record InformationReceived(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the request.")] Guid InformationRequestId,
    [property: Description("Id of the application.")] Guid ApplicationId)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/information_received/jsonschema/1-0-0";
}
