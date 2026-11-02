using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Customers registered a new customer; Accounts and Lending keep a local copy so they can open accounts and loans for it.")]
public sealed record CustomerRegistered(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the customer.")] Guid CustomerId,
    [property: Description("Full name of the customer.")][property: MinLength(1)] string Name,
    [property: Description("ISO 639-1 language for messages to the customer, for example en.")][property: RegularExpression("^[a-z]{2}$")] string PreferredLanguage)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/customer_registered/jsonschema/1-0-0";
}
