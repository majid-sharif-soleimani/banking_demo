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
