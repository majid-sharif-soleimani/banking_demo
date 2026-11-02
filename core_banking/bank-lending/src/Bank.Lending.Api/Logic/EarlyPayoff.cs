using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

/// <summary>What the customer pays to close the loan on a date, and how much future interest the bank gives up.</summary>
public sealed record PayoffQuote(
    Money OutstandingPrincipal,
    Money UnpaidPenalty,
    Money InterestToDate,
    Money MinimumInterest,
    Money WaivedInterest,
    Money Total);

public static class EarlyPayoff
{
    /// <summary>
    /// Total = outstanding principal + unpaid penalty + interest to date + minimum interest (design plan section 2).
    /// Interest to date = unpaid interest of items due by <paramref name="date"/> + the current period's accrual:
    /// annuity: the next item's interest × min(days, 30) / 30; staged: Actual/365 on the principal still scheduled, both
    /// counted from the last due date on or before the date (or disbursement), less interest already prepaid on the next item
    /// (never below 0). Remaining scheduled interest = the later
    /// items' interest minus that accrual. Minimum = min(percent × original principal, cap), but never more than the
    /// remaining scheduled interest; the rest of the remaining interest is waived (ADR 0003).
    /// </summary>
    public static PayoffQuote Quote(IReadOnlyList<ScheduleItem> schedule, LoanTerms terms, DateOnly date)
    {
        var zero = Money.Zero(terms.Currency);
        var due = schedule.Where(i => i.DueDate <= date).ToList();
        var later = schedule.Where(i => i.DueDate > date).OrderBy(i => i.DueDate).ToList();

        var outstandingPrincipal = Sum(schedule.Select(i => i.UnpaidPrincipal), zero);
        var unpaidPenalty = Sum(schedule.Select(i => i.UnpaidPenalty), zero);
        var accrual = later.Count == 0 ? zero : Accrual(terms, later, due.Count == 0 ? terms.DisbursedOn : due.Max(i => i.DueDate), date);
        var interestToDate = Sum(due.Select(i => i.UnpaidInterest), zero) + accrual;

        var remaining = Sum(later.Select(i => i.UnpaidInterest), zero) - accrual;
        if (remaining.Amount < 0m)
        {
            remaining = zero;
        }

        var minimum = Amortization.Round(terms.Principal * terms.PayoffMinPercent, terms);
        minimum = Min(Min(minimum, terms.PayoffCap), remaining);

        return new PayoffQuote(
            outstandingPrincipal,
            unpaidPenalty,
            interestToDate,
            minimum,
            remaining - minimum,
            outstandingPrincipal + unpaidPenalty + interestToDate + minimum);
    }

    private static Money Accrual(LoanTerms terms, List<ScheduleItem> later, DateOnly periodStart, DateOnly date)
    {
        // A quote dated before disbursement accrues nothing (never negative interest).
        var days = Math.Max(0, Amortization.Days(periodStart, date));
        var earned = terms.RepaymentType == RepaymentType.Annuity
            ? Amortization.Round(later[0].InterestDue * (Math.Min(days, 30) / 30m), terms)
            : Amortization.Round(Sum(later.Select(i => i.UnpaidPrincipal), Money.Zero(terms.Currency)) * (terms.AnnualRate * days / 365m), terms);

        // Interest already prepaid on the current item counts against what was earned; more prepaid than earned gives 0, not a refund.
        var owed = earned - later[0].PaidInterest;
        return owed.Amount < 0m ? Money.Zero(terms.Currency) : owed;
    }

    private static Money Sum(IEnumerable<Money> amounts, Money zero) => amounts.Aggregate(zero, (total, amount) => total + amount);

    private static Money Min(Money a, Money b) => a <= b ? a : b;
}
