using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class LoanCostTests
{
    [Fact]
    public void Summarize_CarAnnuity_MatchesHandCalculation()
    {
        // CAR, 200,000 SEK over 36 months: A = 6,170.85 (AmortizationAnnuityTests).
        // Items 1–35 pay 6,170.85 each = 215,979.75; item 36 pays 6,170.79 (rounding difference).
        // Total cost = 215,979.75 + 6,170.79 = 222,150.54; total interest = 222,150.54 − 200,000 = 22,150.54.
        var cost = LoanCost.Summarize(Annuity(Car()), RepaymentType.Annuity);

        cost.Installment.ShouldBe(Sek(6_170.85m));
        cost.TotalInterest.ShouldBe(Sek(22_150.54m));
        cost.TotalCost.ShouldBe(Sek(222_150.54m));
    }

    [Fact]
    public void Summarize_Mortgage_TotalInterestMatchesTheScheduleTest()
    {
        // MORTGAGE, 2,000,000 SEK over 360 months: A = 9,838.80, total interest 1,541,966.48 (AmortizationAnnuityTests).
        var cost = LoanCost.Summarize(Annuity(Mortgage()), RepaymentType.Annuity);

        cost.Installment.ShouldBe(Sek(9_838.80m));
        cost.TotalInterest.ShouldBe(Sek(1_541_966.48m));
        cost.TotalCost.ShouldBe(Sek(3_541_966.48m));
    }

    [Fact]
    public void Summarize_Staged_HasNoSingleInstallmentAndSumsTheStageInterest()
    {
        // PURCHASE, 100,000 SEK at 8.50%, disbursed 2026-01-15, Actual/365:
        // Stage 1 (15 Apr, 30,000): 90 days, 100,000 × 0.085 × 90 / 365 = 2,095.8904 → 2,095.89
        // Stage 2 (15 Jul, 30,000): 91 days,  70,000 × 0.085 × 91 / 365 = 1,483.4247 → 1,483.42
        // Stage 3 (15 Oct, 40,000): 92 days,  40,000 × 0.085 × 92 / 365 =   856.9863 →   856.99
        // Total interest 4,436.30; total cost 104,436.30.
        var schedule = Amortization.BuildStaged(Purchase(), new StagePlan(
        [
            new Stage(new DateOnly(2026, 4, 15), Sek(30_000m)),
            new Stage(new DateOnly(2026, 7, 15), Sek(30_000m)),
            new Stage(new DateOnly(2026, 10, 15), Sek(40_000m)),
        ])).Value;

        var cost = LoanCost.Summarize(schedule, RepaymentType.Staged);

        cost.Installment.ShouldBeNull();
        cost.TotalInterest.ShouldBe(Sek(4_436.30m));
        cost.TotalCost.ShouldBe(Sek(104_436.30m));
    }
}
