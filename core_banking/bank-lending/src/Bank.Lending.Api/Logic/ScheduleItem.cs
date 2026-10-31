using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

public enum ScheduleItemStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
}

/// <summary>
/// One installment (annuity) or stage (staged loan). Immutable: every calculation returns new items (Majid's delegation,
/// ADR 0001). The status follows from the amounts, so it can never disagree with them.
/// </summary>
public sealed record ScheduleItem(
    int Number,
    DateOnly DueDate,
    Money PrincipalDue,
    Money InterestDue,
    Money PenaltyDue,
    Money PaidPrincipal,
    Money PaidInterest,
    Money PaidPenalty)
{
    public static ScheduleItem New(int number, DateOnly dueDate, Money principal, Money interest)
    {
        var zero = Money.Zero(principal.Currency);
        return new ScheduleItem(number, dueDate, principal, interest, zero, zero, zero, zero);
    }

    public Money UnpaidPrincipal => PrincipalDue - PaidPrincipal;

    public Money UnpaidInterest => InterestDue - PaidInterest;

    public Money UnpaidPenalty => PenaltyDue - PaidPenalty;

    public Money Unpaid => UnpaidPrincipal + UnpaidInterest + UnpaidPenalty;

    public Money TotalDue => PrincipalDue + InterestDue + PenaltyDue;

    public ScheduleItemStatus Status =>
        Unpaid.Amount <= 0m ? ScheduleItemStatus.Paid
        : (PaidPrincipal + PaidInterest + PaidPenalty).Amount > 0m ? ScheduleItemStatus.PartiallyPaid
        : ScheduleItemStatus.Unpaid;

    public bool IsPaid => Status == ScheduleItemStatus.Paid;
}
