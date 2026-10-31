using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("A standing order could not run on its date (insufficient funds); it is not retried. Customers emails the customer.")]
public sealed record StandingOrderFailed(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the standing order.")] Guid StandingOrderId,
    [property: Description("Owner of the source account.")] Guid CustomerId,
    [property: Description("Account that should have been debited.")] Guid FromAccountId,
    [property: Description("The amount of the order, in the source account currency.")] Money Amount,
    [property: Description("The business date of the failed run.")] DateOnly RunDate,
    [property: Description("Why it failed, for example insufficient_funds.")][property: RegularExpression("^[a-z_]+$")] string Reason)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/standing_order_failed/jsonschema/1-0-0";
}
