using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class PenaltiesTests
{
    // CAR item 1 is due 15 Feb 2026 with 6,170.85 to pay; grace 5 days, penalty 0.05% per day.
    private static readonly LoanTerms CarTerms = Car();
    private static readonly IReadOnlyList<ScheduleItem> Car36 = Annuity(CarTerms);

    [Theory]
    [InlineData("2026-02-15")] // the due date
    [InlineData("2026-02-16")] // grace day 1
    [InlineData("2026-02-20")] // grace day 5, the last one
    public void ForDay_WithinTheGracePeriod_ChargesNothing(string date) =>
        Penalties.ForDay(Car36, CarTerms, DateOnly.Parse(date)).ShouldAllBe(i => i.PenaltyDue.Amount == 0m);

    [Fact]
    public void ForDay_FirstDayAfterGrace_ChargesTheDailyRateOnTheOverdueAmount()
    {
        // Day 6 = 21 Feb: 6,170.85 × 0.0005 = 3.085425 → 3.09
        var schedule = Penalties.ForDay(Car36, CarTerms, new DateOnly(2026, 2, 21));

        schedule[0].PenaltyDue.ShouldBe(Sek(3.09m));
        schedule.Skip(1).ShouldAllBe(i => i.PenaltyDue.Amount == 0m);
    }

    [Fact]
    public void ForDay_EachDayAddsOneMoreDay_WithoutChargingPenaltyOnPenalty()
    {
        // Day 6 and day 7: each day 6,170.85 × 0.0005 = 3.09 on principal + interest only; total 6.18 (not 3.09 + 3.0915…).
        var day6 = Penalties.ForDay(Car36, CarTerms, new DateOnly(2026, 2, 21));
        var day7 = Penalties.ForDay(day6, CarTerms, new DateOnly(2026, 2, 22));

        day7[0].PenaltyDue.ShouldBe(Sek(6.18m));
    }

    [Fact]
    public void ForDay_PartPaidDuringGrace_ChargesOnlyTheRemainder()
    {
        // 18 Feb (grace) the customer pays 1,158.33, the interest. Day 6: 5,012.52 × 0.0005 = 2.50626 → 2.51
        var paid = Allocation.Apply(Car36, Sek(1_158.33m), new DateOnly(2026, 2, 18)).Value.Schedule;

        Penalties.ForDay(paid, CarTerms, new DateOnly(2026, 2, 21))[0].PenaltyDue.ShouldBe(Sek(2.51m));
    }

    [Fact]
    public void ForDay_PaidInFullDuringGrace_ChargesNothing()
    {
        var paid = Allocation.Apply(Car36, Sek(6_170.85m), new DateOnly(2026, 2, 20)).Value.Schedule;

        Penalties.ForDay(paid, CarTerms, new DateOnly(2026, 2, 21))[0].PenaltyDue.Amount.ShouldBe(0m);
    }

    [Fact]
    public void ForDay_OnlyPenaltyLeftUnpaid_ChargesNothingMore()
    {
        var onlyPenalty = Car36[0] with { PenaltyDue = Sek(3.09m), PaidInterest = Sek(1_158.33m), PaidPrincipal = Sek(5_012.52m) };

        Penalties.ForDay(AllocationTests.Replace(Car36, 0, onlyPenalty), CarTerms, new DateOnly(2026, 2, 25))[0].PenaltyDue.ShouldBe(Sek(3.09m));
    }

    [Fact]
    public void ForDay_TwoOverdueItems_ChargesEachSeparately()
    {
        // 21 Mar: item 1 overdue since 15 Feb (3.09), item 2 due 15 Mar, day 6 → 6,170.85 × 0.0005 = 3.09.
        var schedule = Penalties.ForDay(Car36, CarTerms, new DateOnly(2026, 3, 21));

        schedule[0].PenaltyDue.ShouldBe(Sek(3.09m));
        schedule[1].PenaltyDue.ShouldBe(Sek(3.09m));
    }

    [Fact]
    public void ForDay_MortgageProduct_UsesItsOwnRate()
    {
        // MORTGAGE item 1: 9,838.80 × 0.0003 = 2.95164 → 2.95
        var terms = Mortgage();

        Penalties.ForDay(Annuity(terms), terms, new DateOnly(2026, 2, 21))[0].PenaltyDue.ShouldBe(Sek(2.95m));
    }

    [Fact]
    public void ForDay_PurchaseProduct_ChargesOnTheStage()
    {
        // PURCHASE stage 1 due 15 Apr 2026: 25,000 + 2,095.89 = 27,095.89 × 0.0005 = 13.547945 → 13.55 on day 6 (21 Apr).
        var terms = Purchase();
        var schedule = Amortization.BuildStaged(terms, new StagePlan([
            new Stage(new DateOnly(2026, 4, 15), Sek(25_000m)), new Stage(new DateOnly(2026, 7, 15), Sek(75_000m))])).Value;

        Penalties.ForDay(schedule, terms, new DateOnly(2026, 4, 21))[0].PenaltyDue.ShouldBe(Sek(13.55m));
    }

    [Fact]
    public void ForDay_ZeroGraceDays_ChargesFromTheDayAfterTheDueDate()
    {
        var terms = CarTerms with { GraceDays = 0 };

        Penalties.ForDay(Car36, terms, new DateOnly(2026, 2, 15))[0].PenaltyDue.Amount.ShouldBe(0m);
        Penalties.ForDay(Car36, terms, new DateOnly(2026, 2, 16))[0].PenaltyDue.ShouldBe(Sek(3.09m));
    }
}
