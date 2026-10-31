using Bank.Common;
using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

/// <summary>Builds a loan's repayment schedule at disbursement (design plan section 2, Loans).</summary>
public static class Amortization
{
    /// <summary>
    /// Equal monthly installments. A = P·r(1+r)^n / ((1+r)^n − 1) with r = annual rate / 12 (30/360), rounded to the
    /// currency's minor unit. Each item: interest = round(outstanding · r), principal = A − interest. The last item repays
    /// whatever principal is left, so the principal sums exactly to P and the rounding difference lands there.
    /// Due dates: the first due date, then the same day each following month (the month's last day when it is shorter).
    /// </summary>
    public static Result<IReadOnlyList<ScheduleItem>> BuildAnnuity(LoanTerms terms)
    {
        if (Validate(terms, RepaymentType.Annuity) is { } error)
        {
            return error;
        }

        var n = terms.TermMonths;
        var r = terms.AnnualRate / 12m;
        var payment = Round(r == 0m
            ? terms.Principal * (1m / n)
            : terms.Principal * (r * DecimalMath.Pow(1m + r, n) / (DecimalMath.Pow(1m + r, n) - 1m)), terms);

        var items = new List<ScheduleItem>(n);
        var outstanding = terms.Principal;
        for (var number = 1; number <= n; number++)
        {
            var interest = Round(outstanding * r, terms);
            var principal = number == n ? outstanding : payment - interest;
            items.Add(ScheduleItem.New(number, DueDate(terms, number), principal, interest));
            outstanding -= principal;
        }

        return items;
    }

    /// <summary>
    /// Agreed stages. Each stage repays its principal share plus interest on the principal outstanding before it,
    /// Actual/365 from the previous due date (or disbursement) to this one: I = outstanding · R · days / 365, rounded.
    /// A leap year still divides by 365 (design plan section 2). The plan must have 1..MaxStages stages in the loan
    /// currency, positive shares summing exactly to the principal, and dates strictly after disbursement, increasing,
    /// and not after disbursement + term.
    /// </summary>
    public static Result<IReadOnlyList<ScheduleItem>> BuildStaged(LoanTerms terms, StagePlan plan)
    {
        if ((Validate(terms, RepaymentType.Staged) ?? ValidatePlan(terms, plan)) is { } error)
        {
            return error;
        }

        var items = new List<ScheduleItem>(plan.Stages.Count);
        var outstanding = terms.Principal;
        var previous = terms.DisbursedOn;
        foreach (var (stage, index) in plan.Stages.Select((stage, index) => (stage, index)))
        {
            var interest = Round(outstanding * (terms.AnnualRate * Days(previous, stage.DueDate) / 365m), terms);
            items.Add(ScheduleItem.New(index + 1, stage.DueDate, stage.PrincipalShare, interest));
            outstanding -= stage.PrincipalShare;
            previous = stage.DueDate;
        }

        return items;
    }

    /// <summary>Actual days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static int Days(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    private static Error? ValidatePlan(LoanTerms terms, StagePlan plan)
    {
        var stages = plan.Stages;
        if (stages.Count == 0)
        {
            return LoanErrors.NoStages;
        }

        if (terms.MaxStages is { } max && stages.Count > max)
        {
            return LoanErrors.TooManyStages;
        }

        if (stages.Any(s => s.PrincipalShare.Currency != terms.Currency))
        {
            return LoanErrors.CurrencyMismatch;
        }

        if (stages.Any(s => s.PrincipalShare.Amount <= 0m))
        {
            return LoanErrors.ShareNotPositive;
        }

        var previous = terms.DisbursedOn;
        foreach (var stage in stages)
        {
            if (stage.DueDate <= previous)
            {
                return LoanErrors.DatesNotIncreasing;
            }

            previous = stage.DueDate;
        }

        if (stages[^1].DueDate > terms.MaturityLimit)
        {
            return LoanErrors.StagePastTerm;
        }

        return stages.Sum(s => s.PrincipalShare.Amount) != terms.Principal.Amount ? LoanErrors.SharesDoNotSumToPrincipal : null;
    }

    /// <summary>
    /// Due date of item <paramref name="number"/>, always counted from the anchor date, never from the previous due date:
    /// disbursed 31 Jan gives 29 Feb (leap year), 31 Mar, 30 Apr. An explicit first due date is the anchor instead.
    /// </summary>
    internal static DateOnly DueDate(LoanTerms terms, int number) =>
        terms.FirstDueDate is { } first ? first.AddMonths(number - 1) : terms.DisbursedOn.AddMonths(number);

    internal static Money Round(Money money, LoanTerms terms) => MoneyRounding.Round(money, terms.MinorUnits);

    private static Error? Validate(LoanTerms terms, RepaymentType expected)
    {
        if (terms.RepaymentType != expected)
        {
            return LoanErrors.WrongRepaymentType;
        }

        if (terms.Principal.Amount <= 0m)
        {
            return LoanErrors.PrincipalNotPositive;
        }

        if (terms.TermMonths < 1)
        {
            return LoanErrors.TermNotPositive;
        }

        if (terms.AnnualRate < 0m)
        {
            return LoanErrors.RateNegative;
        }

        if (terms.PayoffCap.Currency != terms.Currency)
        {
            return LoanErrors.CurrencyMismatch;
        }

        return expected == RepaymentType.Annuity && terms.EffectiveFirstDueDate <= terms.DisbursedOn
            ? LoanErrors.FirstDueNotAfterDisbursement
            : null;
    }
}
