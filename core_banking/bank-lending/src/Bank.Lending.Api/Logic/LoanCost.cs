using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

/// <summary>
/// What a new schedule costs the borrower: the regular installment, the interest over the whole term and the total paid.
/// Read from the schedule alone, so the figures always agree with the items shown next to them.
/// </summary>
/// <param name="Installment">The annuity amount A (the first item; only the last item may differ by rounding). Staged loans have none.</param>
public sealed record LoanCost(Money? Installment, Money TotalInterest, Money TotalCost)
{
    /// <summary>Total cost = principal + interest of every item. A new schedule has no penalties.</summary>
    public static LoanCost Summarize(IReadOnlyList<ScheduleItem> schedule, RepaymentType repaymentType)
    {
        var zero = Money.Zero(schedule[0].PrincipalDue.Currency);
        var interest = schedule.Aggregate(zero, (sum, item) => sum + item.InterestDue);
        var principal = schedule.Aggregate(zero, (sum, item) => sum + item.PrincipalDue);
        return new LoanCost(
            repaymentType == RepaymentType.Annuity ? schedule[0].PrincipalDue + schedule[0].InterestDue : null,
            interest,
            principal + interest);
    }
}
