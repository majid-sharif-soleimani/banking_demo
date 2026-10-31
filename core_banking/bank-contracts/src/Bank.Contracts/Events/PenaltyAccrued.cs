using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Lending added late penalties to a loan at end of day; reporting only, no journal entry.")]
public sealed record PenaltyAccrued(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Penalty added for the day.")] Money Amount,
    [property: Description("Business date of the penalty.")] DateOnly Date)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/penalty_accrued/jsonschema/1-0-0";
}
