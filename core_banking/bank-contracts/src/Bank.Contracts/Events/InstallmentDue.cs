using System.ComponentModel;

namespace Bank.Contracts.Events;

[Description("Lending asks Accounts to collect a due installment in full from the repayment account.")]
public sealed record InstallmentDue(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the loan.")] Guid LoanId,
    [property: Description("Unique id of this collection attempt; Accounts debits at most once per id.")] Guid CollectionId,
    [property: Description("Installment number.")] int ScheduleItemNumber,
    [property: Description("Repayment account.")] Guid AccountId,
    [property: Description("Full unpaid amount of the installment, penalty included.")] Money Amount,
    [property: Description("Due date of the installment.")] DateOnly DueDate)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/installment_due/jsonschema/1-0-0";
}
