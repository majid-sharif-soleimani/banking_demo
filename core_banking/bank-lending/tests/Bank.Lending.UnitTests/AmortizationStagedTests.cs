using Bank.Contracts;
using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class AmortizationStagedTests
{
    private static StagePlan Plan(params (string Date, decimal Share)[] stages) =>
        new([.. stages.Select(s => new Stage(DateOnly.Parse(s.Date), Sek(s.Share)))]);

    private static readonly StagePlan FourQuarters = Plan(
        ("2026-04-15", 25_000m), ("2026-07-15", 25_000m), ("2026-10-15", 25_000m), ("2027-01-15", 25_000m));

    [Fact]
    public void BuildStaged_PurchaseProduct_MatchesHandCalculation()
    {
        // P = 100,000 SEK, R = 8.50%, disbursed 15 Jan 2026, four quarterly stages of 25,000. Interest = outstanding × R × days / 365.
        // Stage 1: 15 Jan → 15 Apr = 90 days: 100,000 × 0.085 × 90 / 365 = 2,095.8904 → 2,095.89
        // Stage 2: 15 Apr → 15 Jul = 91 days:  75,000 × 0.085 × 91 / 365 = 1,589.3836 → 1,589.38
        // Stage 3: 15 Jul → 15 Oct = 92 days:  50,000 × 0.085 × 92 / 365 = 1,071.2329 → 1,071.23
        // Stage 4: 15 Oct → 15 Jan = 92 days:  25,000 × 0.085 × 92 / 365 =   535.6164 →   535.62
        var schedule = Amortization.BuildStaged(Purchase(), FourQuarters).Value;

        schedule.Select(i => i.InterestDue.Amount).ShouldBe([2_095.89m, 1_589.38m, 1_071.23m, 535.62m]);
        schedule.Select(i => i.PrincipalDue.Amount).ShouldBe([25_000m, 25_000m, 25_000m, 25_000m]);
        schedule.Select(i => i.Number).ShouldBe([1, 2, 3, 4]);
        schedule[3].DueDate.ShouldBe(new DateOnly(2027, 1, 15));
    }

    [Fact]
    public void BuildStaged_Feb29InsideAStage_CountsTheDayButStillDividesBy365()
    {
        // Disbursed 15 Jan 2028 (leap year): 15 Jan → 15 Apr = 91 days including 29 Feb.
        // 100,000 × 0.085 × 91 / 365 = 2,119.1781 → 2,119.18 (not / 366)
        // Then 15 Apr → 15 Jul = 91 days on 60,000: 1,271.5068 → 1,271.51
        var terms = Purchase(disbursedOn: new DateOnly(2028, 1, 15));

        var schedule = Amortization.BuildStaged(terms, Plan(("2028-04-15", 40_000m), ("2028-07-15", 60_000m))).Value;

        schedule.Select(i => i.InterestDue.Amount).ShouldBe([2_119.18m, 1_271.51m]);
    }

    [Fact]
    public void BuildStaged_SingleStage_RepaysEverythingWithInterestForTheWholePeriod()
    {
        // 15 Jan → 15 Jul 2026 = 181 days: 100,000 × 0.085 × 181 / 365 = 4,215.0685 → 4,215.07
        var schedule = Amortization.BuildStaged(Purchase(), Plan(("2026-07-15", 100_000m))).Value;

        schedule.ShouldHaveSingleItem().InterestDue.ShouldBe(Sek(4_215.07m));
    }

    [Fact]
    public void BuildStaged_ZeroRate_HasNoInterest() =>
        Amortization.BuildStaged(Purchase() with { AnnualRate = 0m }, FourQuarters).Value
            .ShouldAllBe(i => i.InterestDue.Amount == 0m);

    [Fact]
    public void BuildStaged_StageOnTheLastDayOfTheTerm_IsAllowed() =>
        // Term 12 months from 15 Jan 2026 ends 15 Jan 2027, the date of stage 4.
        Amortization.BuildStaged(Purchase(), FourQuarters).IsSuccess.ShouldBeTrue();

    [Fact]
    public void BuildStaged_MoreStagesThanTheProductAllows_IsRejected() =>
        Amortization.BuildStaged(Purchase(), Plan(
                ("2026-03-15", 20_000m), ("2026-05-15", 20_000m), ("2026-07-15", 20_000m), ("2026-09-15", 20_000m), ("2026-11-15", 20_000m)))
            .Error.ShouldBe(LoanErrors.TooManyStages);

    [Theory]
    [InlineData(25_000, 25_000, 25_000, 24_999.99)] // one öre short
    [InlineData(25_000, 25_000, 25_000, 25_000.01)] // one öre too much
    public void BuildStaged_SharesNotSummingToThePrincipal_IsRejected(decimal a, decimal b, decimal c, decimal d) =>
        Amortization.BuildStaged(Purchase(), Plan(("2026-04-15", a), ("2026-07-15", b), ("2026-10-15", c), ("2027-01-15", d)))
            .Error.ShouldBe(LoanErrors.SharesDoNotSumToPrincipal);

    [Fact]
    public void BuildStaged_ANonPositiveShare_IsRejected() =>
        Amortization.BuildStaged(Purchase(), Plan(("2026-04-15", 100_000m), ("2026-07-15", 0m)))
            .Error.ShouldBe(LoanErrors.ShareNotPositive);

    [Theory]
    [InlineData("2026-07-15", "2026-04-15")] // decreasing
    [InlineData("2026-04-15", "2026-04-15")] // the same day twice
    [InlineData("2026-01-15", "2026-07-15")] // on the disbursement date
    [InlineData("2026-01-10", "2026-07-15")] // before disbursement
    public void BuildStaged_DatesNotStrictlyIncreasingAfterDisbursement_AreRejected(string first, string second) =>
        Amortization.BuildStaged(Purchase(), Plan((first, 50_000m), (second, 50_000m)))
            .Error.ShouldBe(LoanErrors.DatesNotIncreasing);

    [Fact]
    public void BuildStaged_StageAfterTheTerm_IsRejected() =>
        Amortization.BuildStaged(Purchase(), Plan(("2026-06-15", 50_000m), ("2027-01-16", 50_000m)))
            .Error.ShouldBe(LoanErrors.StagePastTerm);

    [Fact]
    public void BuildStaged_EmptyPlan_IsRejected() =>
        Amortization.BuildStaged(Purchase(), new StagePlan([])).Error.ShouldBe(LoanErrors.NoStages);

    [Fact]
    public void BuildStaged_ShareInAnotherCurrency_IsRejected() =>
        Amortization.BuildStaged(Purchase(), new StagePlan([new Stage(new DateOnly(2026, 7, 15), new Money(100_000m, "EUR"))]))
            .Error.ShouldBe(LoanErrors.CurrencyMismatch);

    [Fact]
    public void BuildStaged_AnnuityTerms_ReturnsWrongRepaymentType() =>
        Amortization.BuildStaged(Car(), FourQuarters).Error.ShouldBe(LoanErrors.WrongRepaymentType);

    [Fact]
    public void BuildStaged_NoMaxStagesOnTheProduct_AllowsAnyNumber() =>
        Amortization.BuildStaged(Purchase() with { MaxStages = null }, Plan(
                ("2026-03-15", 20_000m), ("2026-05-15", 20_000m), ("2026-07-15", 20_000m), ("2026-09-15", 20_000m), ("2026-11-15", 20_000m)))
            .IsSuccess.ShouldBeTrue();
}
