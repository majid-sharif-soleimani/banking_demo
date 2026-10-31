using Bank.Lending.Api.Logic;
using Bank.Lending.UnitTests.Support;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class AmortizationAnnuityTests
{
    [Fact]
    public void BuildAnnuity_CarProduct_MatchesHandCalculation()
    {
        // P = 200,000 SEK, R = 6.95%, n = 36, r = 0.0695 / 12 = 0.0057916667
        // A = P·r(1+r)^n / ((1+r)^n − 1) = 6,170.8507… → 6,170.85
        // Item 1: interest = round(200,000 × 0.0057916667) = round(1,158.3333) = 1,158.33; principal = 6,170.85 − 1,158.33 = 5,012.52
        // Item 2: outstanding = 194,987.48; interest = round(1,129.2992) = 1,129.30; principal = 5,041.55
        var schedule = Annuity(Car());

        schedule.Count.ShouldBe(36);
        schedule[0].ShouldSatisfyAllConditions(
            i => i.Number.ShouldBe(1),
            i => i.InterestDue.ShouldBe(Sek(1_158.33m)),
            i => i.PrincipalDue.ShouldBe(Sek(5_012.52m)));
        schedule[1].InterestDue.ShouldBe(Sek(1_129.30m));
        schedule[1].PrincipalDue.ShouldBe(Sek(5_041.55m));
    }

    [Fact]
    public void BuildAnnuity_CarProduct_LastItemTakesTheRoundingDifference()
    {
        // Items 1–35 repay 6,170.85 each. Before item 36 the outstanding principal is 6,135.26.
        // Item 36: interest = round(6,135.26 × 0.0057916667) = round(35.5334) = 35.53, principal = 6,135.26 (all that is left),
        // total 6,170.79: six öre less than A, the accumulated rounding difference.
        var schedule = Annuity(Car());

        schedule[^1].PrincipalDue.ShouldBe(Sek(6_135.26m));
        schedule[^1].InterestDue.ShouldBe(Sek(35.53m));
        schedule.Take(35).ShouldAllBe(i => i.PrincipalDue.Amount + i.InterestDue.Amount == 6_170.85m);
        schedule.Sum(i => i.PrincipalDue.Amount).ShouldBe(200_000m);
    }

    [Fact]
    public void BuildAnnuity_MortgageProduct_MatchesHandCalculation()
    {
        // P = 2,000,000 SEK, R = 4.25%, n = 360, r = 0.0425 / 12 = 0.0035416667
        // A = 9,838.7957… → 9,838.80
        // Item 1: interest = round(2,000,000 × 0.0035416667) = round(7,083.3333) = 7,083.33; principal = 2,755.47
        // Item 360: outstanding 9,802.56; interest = round(34.7174) = 34.72; principal 9,802.56; total 9,837.28
        // Total interest over 30 years: 1,541,966.48
        var schedule = Annuity(Mortgage());

        schedule.Count.ShouldBe(360);
        schedule[0].InterestDue.ShouldBe(Sek(7_083.33m));
        schedule[0].PrincipalDue.ShouldBe(Sek(2_755.47m));
        schedule[^1].PrincipalDue.ShouldBe(Sek(9_802.56m));
        schedule[^1].InterestDue.ShouldBe(Sek(34.72m));
        schedule.Sum(i => i.PrincipalDue.Amount).ShouldBe(2_000_000m);
        schedule.Sum(i => i.InterestDue.Amount).ShouldBe(1_541_966.48m);
    }

    [Fact]
    public void BuildAnnuity_ZeroRate_SplitsThePrincipalEvenlyWithTheRemainderLast()
    {
        // r = 0: A = P / n = 1,000 / 3 = 333.333… → 333.33; items 333.33, 333.33, and 1,000 − 666.66 = 333.34; no interest.
        var schedule = Annuity(Car(principal: 1_000m, months: 3) with { AnnualRate = 0m });

        schedule.Select(i => i.PrincipalDue.Amount).ShouldBe([333.33m, 333.33m, 333.34m]);
        schedule.ShouldAllBe(i => i.InterestDue.Amount == 0m);
    }

    [Fact]
    public void BuildAnnuity_OneMonthTerm_RepaysEverythingAtOnce()
    {
        // n = 1: principal 20,000, interest = round(20,000 × 0.0057916667) = round(115.8333) = 115.83
        var schedule = Annuity(Car(principal: 20_000m, months: 1));

        schedule.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            i => i.PrincipalDue.ShouldBe(Sek(20_000m)),
            i => i.InterestDue.ShouldBe(Sek(115.83m)),
            i => i.DueDate.ShouldBe(new DateOnly(2026, 2, 15)));
    }

    [Fact]
    public void BuildAnnuity_DueDates_FallOnTheSameDayEachMonth()
    {
        var schedule = Annuity(Car(months: 3));

        schedule.Select(i => i.DueDate).ShouldBe([new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)]);
    }

    [Fact]
    public void BuildAnnuity_DisbursedOnTheLastDayOfAMonth_UsesTheLastDayOfShorterMonths()
    {
        // Disbursed 31 Jan 2028 (a leap year): first due 29 Feb, then 31 Mar, 30 Apr, 31 May; each counted from the first due date's day 31.
        var schedule = Annuity(Car(months: 4, disbursedOn: new DateOnly(2028, 1, 31)));

        schedule.Select(i => i.DueDate).ShouldBe(
            [new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 31), new DateOnly(2028, 4, 30), new DateOnly(2028, 5, 31)]);
    }

    [Fact]
    public void BuildAnnuity_ExplicitFirstDueDate_IsUsed()
    {
        var schedule = Annuity(Car(months: 2) with { FirstDueDate = new DateOnly(2026, 3, 1) });

        schedule.Select(i => i.DueDate).ShouldBe([new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1)]);
    }

    [Fact]
    public void BuildAnnuity_NewSchedule_IsUnpaidWithoutPenalty()
    {
        Annuity(Car(months: 2)).ShouldAllBe(i =>
            i.Status == ScheduleItemStatus.Unpaid && i.PenaltyDue.Amount == 0m && i.PaidPrincipal.Amount == 0m);
    }

    [Theory]
    [InlineData(0, 36, 0.0695, "principal_not_positive")]
    [InlineData(-1, 36, 0.0695, "principal_not_positive")]
    [InlineData(1000, 0, 0.0695, "term_not_positive")]
    [InlineData(1000, 12, -0.01, "rate_negative")]
    public void BuildAnnuity_InvalidTerms_ReturnsTheErrorCode(int principal, int months, double rate, string code)
    {
        var result = Amortization.BuildAnnuity(Car(principal: principal, months: months) with { AnnualRate = (decimal)rate });

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void BuildAnnuity_StagedTerms_ReturnsWrongRepaymentType() =>
        Amortization.BuildAnnuity(Purchase()).Error.ShouldBe(LoanErrors.WrongRepaymentType);

    [Fact]
    public void BuildAnnuity_FirstDueNotAfterDisbursement_ReturnsError() =>
        Amortization.BuildAnnuity(Car() with { FirstDueDate = new DateOnly(2026, 1, 15) }).Error
            .ShouldBe(LoanErrors.FirstDueNotAfterDisbursement);

    [Fact]
    public void ScheduleItem_Status_FollowsTheAmounts()
    {
        var item = ScheduleItem.New(1, new DateOnly(2026, 2, 15), Sek(100m), Sek(10m));

        item.Status.ShouldBe(ScheduleItemStatus.Unpaid);
        (item with { PaidInterest = Sek(10m) }).Status.ShouldBe(ScheduleItemStatus.PartiallyPaid);
        (item with { PaidInterest = Sek(10m), PaidPrincipal = Sek(100m) }).Status.ShouldBe(ScheduleItemStatus.Paid);
        (item with { PaidInterest = Sek(10m), PaidPrincipal = Sek(100m), PenaltyDue = Sek(1m) }).Status.ShouldBe(ScheduleItemStatus.PartiallyPaid);
    }

    [Fact]
    public void BuildAnnuity_PrincipalWithMoreDecimalsThanTheCurrency_IsRejected() =>
        Amortization.BuildAnnuity(Car(principal: 1_000.005m)).Error.ShouldBe(LoanErrors.AmountNotInMinorUnits);

    [Theory]
    [InlineData(-0.0001, 5, 1, 90, 0.01)]  // negative penalty rate
    [InlineData(0.0005, -1, 1, 90, 0.01)]  // negative grace
    [InlineData(0.0005, 5, 0, 90, 0.01)]   // overdue threshold below one day
    [InlineData(0.0005, 5, 91, 90, 0.01)]  // overdue after default
    [InlineData(0.0005, 5, 1, 90, 1.5)]    // payoff percent above 100%
    public void BuildAnnuity_InconsistentProductParameters_AreRejected(double penalty, int grace, int overdue, int @default, double payoff) =>
        Amortization.BuildAnnuity(Car() with
        {
            PenaltyDailyRate = (decimal)penalty, GraceDays = grace, OverdueAfterDays = overdue, DefaultAfterDays = @default,
            PayoffMinPercent = (decimal)payoff,
        }).Error.ShouldBe(LoanErrors.InvalidTerms);
}
