using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class EarlyPayoffTests
{
    private static readonly LoanTerms CarTerms = Car();
    private static readonly IReadOnlyList<ScheduleItem> Car36 = Annuity(CarTerms);
    private static readonly IReadOnlyList<ScheduleItem> Car36ItemOnePaid =
        Allocation.Apply(Car36, Sek(6_170.85m), new DateOnly(2026, 2, 15)).Value.Schedule;

    [Fact]
    public void Quote_CarMidPeriod_MatchesHandCalculation()
    {
        // Item 1 paid. Payoff on 1 Mar 2026, 14 days after the 15 Feb due date.
        // Outstanding principal = 200,000 − 5,012.52 = 194,987.48
        // Interest to date = item 2 interest × 14/30 = 1,129.30 × 14 / 30 = 527.0133 → 527.01
        // Remaining scheduled interest = items 2–36 (20,992.21) − 527.01 = 20,465.20
        // Minimum = min(1% × 200,000, 5,000) = 2,000; waived = 20,465.20 − 2,000 = 18,465.20
        // Total = 194,987.48 + 527.01 + 2,000 = 197,514.49
        var quote = EarlyPayoff.Quote(Car36ItemOnePaid, CarTerms, new DateOnly(2026, 3, 1));

        quote.ShouldBe(new PayoffQuote(
            OutstandingPrincipal: Sek(194_987.48m),
            UnpaidPenalty: Sek(0m),
            InterestToDate: Sek(527.01m),
            MinimumInterest: Sek(2_000m),
            WaivedInterest: Sek(18_465.20m),
            Total: Sek(197_514.49m)));
    }

    [Fact]
    public void Quote_OnADueDate_ChargesThatItemsInterestInFullAndAccruesNothingNew()
    {
        // Payoff on 15 Mar, item 2's due date, item 2 unpaid: its interest 1,129.30 is due in full; item 3's period starts today (0 days).
        // Remaining scheduled interest = items 3–36 = 20,992.21 − 1,129.30 = 19,862.91; minimum 2,000; waived 17,862.91.
        // Total = 194,987.48 + 1,129.30 + 2,000 = 198,116.78
        var quote = EarlyPayoff.Quote(Car36ItemOnePaid, CarTerms, new DateOnly(2026, 3, 15));

        quote.InterestToDate.ShouldBe(Sek(1_129.30m));
        quote.WaivedInterest.ShouldBe(Sek(17_862.91m));
        quote.Total.ShouldBe(Sek(198_116.78m));
    }

    [Fact]
    public void Quote_RemainingInterestBelowTheMinimum_GivesNoDiscount()
    {
        // 20,000 over 3 months: items 6,628.21/115.83, 6,666.60/77.44, 6,705.19/38.83. Items 1 and 2 paid. Payoff on 25 Mar (10 days).
        // Interest to date = 38.83 × 10 / 30 = 12.9433 → 12.94; remaining scheduled = 38.83 − 12.94 = 25.89
        // Minimum = min(1% × 20,000 = 200, 5,000) = 200 > 25.89 → minimum = 25.89, waived = 0
        // Total = 6,705.19 + 12.94 + 25.89 = 6,744.02, the full last installment minus nothing: no discount.
        var terms = Car(principal: 20_000m, months: 3);
        var schedule = Annuity(terms);
        var paid = Allocation.Apply(schedule, Sek(6_744.04m + 6_744.04m), new DateOnly(2026, 3, 15)).Value.Schedule;

        var quote = EarlyPayoff.Quote(paid, terms, new DateOnly(2026, 3, 25));

        quote.MinimumInterest.ShouldBe(Sek(25.89m));
        quote.WaivedInterest.ShouldBe(Sek(0m));
        quote.Total.ShouldBe(Sek(6_744.02m));
    }

    [Fact]
    public void Quote_LargeLoan_MinimumIsCappedByTheProduct()
    {
        // 600,000: 1% = 6,000 > cap 5,000 → minimum 5,000.
        var terms = Car(principal: 600_000m);

        EarlyPayoff.Quote(Annuity(terms), terms, new DateOnly(2026, 2, 1)).MinimumInterest.ShouldBe(Sek(5_000m));
    }

    [Fact]
    public void Quote_OverdueItemWithPenalty_ChargesPenaltyAndOverdueInterest()
    {
        // Item 1 unpaid with penalty 6.18; payoff on 1 Mar: its interest 1,158.33 is due, plus item 2's accrual 1,129.30 × 14/30 = 527.01.
        var schedule = AllocationTests.Replace(Car36, 0, Car36[0] with { PenaltyDue = Sek(6.18m) });

        var quote = EarlyPayoff.Quote(schedule, CarTerms, new DateOnly(2026, 3, 1));

        quote.OutstandingPrincipal.ShouldBe(Sek(200_000m));
        quote.UnpaidPenalty.ShouldBe(Sek(6.18m));
        quote.InterestToDate.ShouldBe(Sek(1_158.33m + 527.01m));
    }

    [Fact]
    public void Quote_StagedLoan_AccruesActual365OnTheOutstandingPrincipal()
    {
        // PURCHASE, four quarters of 25,000. Payoff on 14 Feb 2026, 30 days after disbursement:
        // interest to date = 100,000 × 0.085 × 30 / 365 = 698.6301 → 698.63
        // remaining scheduled = (2,095.89 + 1,589.38 + 1,071.23 + 535.62 = 5,292.12) − 698.63 = 4,593.49
        // minimum = min(1% × 100,000 = 1,000, 3,000) = 1,000; waived = 3,593.49; total = 100,000 + 698.63 + 1,000 = 101,698.63
        var terms = Purchase();
        var schedule = Amortization.BuildStaged(terms, new StagePlan([
            new Stage(new DateOnly(2026, 4, 15), Sek(25_000m)), new Stage(new DateOnly(2026, 7, 15), Sek(25_000m)),
            new Stage(new DateOnly(2026, 10, 15), Sek(25_000m)), new Stage(new DateOnly(2027, 1, 15), Sek(25_000m))])).Value;

        var quote = EarlyPayoff.Quote(schedule, terms, new DateOnly(2026, 2, 14));

        quote.InterestToDate.ShouldBe(Sek(698.63m));
        quote.MinimumInterest.ShouldBe(Sek(1_000m));
        quote.WaivedInterest.ShouldBe(Sek(3_593.49m));
        quote.Total.ShouldBe(Sek(101_698.63m));
    }

    [Fact]
    public void Quote_AnnuityPeriodLongerThan30Days_AccruesAtMostOnePeriod()
    {
        // Item 2 period 15 Feb → 15 Mar has 28 days; item 3 period 15 Mar → 15 Apr has 31. On 14 Apr (30 days): 30/30 = all of item 3's
        // interest, the cap; the accrual never exceeds the period's interest.
        var paid = Allocation.Apply(Car36, Sek(6_170.85m * 2), new DateOnly(2026, 3, 15)).Value.Schedule;

        var quote = EarlyPayoff.Quote(paid, CarTerms, new DateOnly(2026, 4, 14));

        quote.InterestToDate.ShouldBe(Car36[2].InterestDue);
    }

    [Fact]
    public void Quote_FullyPaidLoan_IsZero()
    {
        var terms = Car(principal: 20_000m, months: 1);
        var paid = Allocation.Apply(Annuity(terms), Sek(20_115.83m), new DateOnly(2026, 2, 15)).Value.Schedule;

        EarlyPayoff.Quote(paid, terms, new DateOnly(2026, 2, 20)).Total.ShouldBe(Sek(0m));
    }

    [Fact]
    public void Quote_MortgageMidPeriod_MatchesHandCalculation()
    {
        // MORTGAGE, item 1 paid (principal 2,755.47). Payoff on 1 Mar 2026, 14 days into item 2's period.
        // Outstanding principal = 2,000,000 − 2,755.47 = 1,997,244.53
        // Item 2 interest = round(1,997,244.53 × 0.0425 / 12) = 7,073.57; to date = 7,073.57 × 14 / 30 = 3,300.9993 → 3,301.00
        // Remaining scheduled = items 2–360 (1,541,966.48 − 7,083.33 = 1,534,883.15) − 3,301.00 = 1,531,582.15
        // Minimum = min(1% × 2,000,000 = 20,000, cap 20,000) = 20,000; waived = 1,511,582.15
        // Total = 1,997,244.53 + 3,301.00 + 20,000 = 2,020,545.53
        var terms = Mortgage();
        var paid = Allocation.Apply(Annuity(terms), Sek(9_838.80m), new DateOnly(2026, 2, 15)).Value.Schedule;

        var quote = EarlyPayoff.Quote(paid, terms, new DateOnly(2026, 3, 1));

        quote.ShouldBe(new PayoffQuote(Sek(1_997_244.53m), Sek(0m), Sek(3_301.00m), Sek(20_000m), Sek(1_511_582.15m), Sek(2_020_545.53m)));
    }

    [Fact]
    public void Quote_ZeroRate_IsTheOutstandingPrincipalWithoutMinimum()
    {
        // No interest at all: remaining scheduled interest 0, so minimum = min(10, 5,000, 0) = 0 and nothing is waived.
        var terms = Car(principal: 1_000m, months: 3) with { AnnualRate = 0m };

        var quote = EarlyPayoff.Quote(Annuity(terms), terms, new DateOnly(2026, 2, 1));

        quote.Total.ShouldBe(Sek(1_000m));
        quote.MinimumInterest.ShouldBe(Sek(0m));
        quote.WaivedInterest.ShouldBe(Sek(0m));
    }

    [Fact]
    public void Quote_BeforeDisbursement_AccruesNothing()
    {
        var quote = EarlyPayoff.Quote(Car36, CarTerms, new DateOnly(2026, 1, 10));

        quote.InterestToDate.ShouldBe(Sek(0m));
        quote.Total.ShouldBeGreaterThanOrEqualTo(quote.OutstandingPrincipal);
    }

    [Fact]
    public void Quote_FirstPeriodLongerThanAMonth_AccruesAtMostOnePeriod()
    {
        // First due date 15 Apr for a loan disbursed 15 Jan: on 10 Apr (85 days) the accrual is capped at 30/30 of item 1's interest.
        var terms = Car(months: 12) with { FirstDueDate = new DateOnly(2026, 4, 15) };
        var schedule = Annuity(terms);

        EarlyPayoff.Quote(schedule, terms, new DateOnly(2026, 4, 10)).InterestToDate.ShouldBe(schedule[0].InterestDue);
    }
}
