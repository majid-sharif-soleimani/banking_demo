using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("Lending allocated a payment to a loan; Ledger posts it with this split.")]
public sealed record LoanPaymentAllocated(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Id of the payment (collection or card payment).")] Guid PaymentId,
    [property: Description("Account, Card or Payoff.")][property: RegularExpression("^(Account|Card|Payoff)$")] string Source,
    [property: Description("Part that repaid principal.")] Money Principal,
    [property: Description("Part that paid interest.")] Money Interest,
    [property: Description("Part that paid penalties.")] Money Penalty)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/loan_payment_allocated/jsonschema/1-0-0";
}
