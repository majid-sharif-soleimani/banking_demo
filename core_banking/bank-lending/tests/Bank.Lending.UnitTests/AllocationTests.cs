using Bank.Contracts;
using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class AllocationTests
{
    // CAR 200,000 / 36 months from 15 Jan 2026: item 1 due 15 Feb (principal 5,012.52, interest 1,158.33, total 6,170.85),
    // item 2 due 15 Mar (principal 5,041.55, interest 1,129.30).
    private static readonly IReadOnlyList<ScheduleItem> Car36 = Annuity(Car());
    private static readonly DateOnly Feb15 = new(2026, 2, 15);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);

    [Fact]
    public void Apply_PaymentExactlyEqualToTheItemOnItsDueDate_PaysItInFull()
    {
        var result = Allocation.Apply(Car36, Sek(6_170.85m), Feb15).Value;

        result.Schedule[0].IsPaid.ShouldBeTrue();
        result.Schedule[0].PaidInterest.ShouldBe(Sek(1_158.33m));
        result.Schedule[0].PaidPrincipal.ShouldBe(Sek(5_012.52m));
        result.Unallocated.ShouldBe(Sek(0m));
        result.Allocations.ShouldHaveSingleItem().ShouldBe(new ItemAllocation(1, Sek(0m), Sek(1_158.33m), Sek(5_012.52m)));
    }

    [Fact]
    public void Apply_PartialPayment_PaysInterestBeforePrincipal()
    {
        // 2,000 on item 1: interest 1,158.33 first, then principal 2,000 − 1,158.33 = 841.67.
        var item = Allocation.Apply(Car36, Sek(2_000m), Feb15).Value.Schedule[0];

        item.PaidInterest.ShouldBe(Sek(1_158.33m));
        item.PaidPrincipal.ShouldBe(Sek(841.67m));
        item.UnpaidPrincipal.ShouldBe(Sek(4_170.85m));
        item.Status.ShouldBe(ScheduleItemStatus.PartiallyPaid);
    }

    [Fact]
    public void Apply_PaymentSmallerThanTheInterest_PaysOnlyInterest()
    {
        var item = Allocation.Apply(Car36, Sek(100m), Feb15).Value.Schedule[0];

        item.PaidInterest.ShouldBe(Sek(100m));
        item.PaidPrincipal.ShouldBe(Sek(0m));
    }

    [Fact]
    public void Apply_ItemWithPenalty_PaysPenaltyThenInterestThenPrincipal()
    {
        // Penalty 3.09 on item 1. 6,170.85 pays penalty 3.09, interest 1,158.33, principal 6,170.85 − 3.09 − 1,158.33 = 5,009.43;
        // 3.09 principal stays unpaid.
        var withPenalty = Replace(Car36, 0, Car36[0] with { PenaltyDue = Sek(3.09m) });

        var result = Allocation.Apply(withPenalty, Sek(6_170.85m), new DateOnly(2026, 2, 21)).Value;

        result.Allocations.ShouldHaveSingleItem().ShouldBe(new ItemAllocation(1, Sek(3.09m), Sek(1_158.33m), Sek(5_009.43m)));
        result.Schedule[0].UnpaidPrincipal.ShouldBe(Sek(3.09m));
    }

    [Fact]
    public void Apply_TwoItemsDue_PaysTheOldestFirst()
    {
        // On 15 Mar both items are due. 8,000 pays item 1 (6,170.85) in full, then item 2: interest 1,129.30, principal 699.85.
        var result = Allocation.Apply(Car36, Sek(8_000m), Mar15).Value;

        result.Schedule[0].IsPaid.ShouldBeTrue();
        result.Schedule[1].PaidInterest.ShouldBe(Sek(1_129.30m));
        result.Schedule[1].PaidPrincipal.ShouldBe(Sek(699.85m));
        result.Allocations.Select(a => a.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void Apply_PenaltyOnAnOlderItem_IsPaidBeforeInterestOnANewerOne()
    {
        // Oldest item first, and within it penalty → interest → principal: item 1's penalty is paid before item 2 is touched.
        var schedule = Replace(Car36, 0, Car36[0] with { PenaltyDue = Sek(50m) });

        var result = Allocation.Apply(schedule, Sek(1_000m), Mar15).Value;

        result.Schedule[0].PaidPenalty.ShouldBe(Sek(50m));
        result.Schedule[0].PaidInterest.ShouldBe(Sek(950m));
        result.Schedule[1].PaidInterest.ShouldBe(Sek(0m));
    }

    [Fact]
    public void Apply_Overpayment_IsReturnedAndFutureItemsAreNotPrepaid()
    {
        // On 15 Feb only item 1 is due. 7,000 − 6,170.85 = 829.15 is returned; item 2 (due 15 Mar) stays unpaid.
        var result = Allocation.Apply(Car36, Sek(7_000m), Feb15).Value;

        result.Unallocated.ShouldBe(Sek(829.15m));
        result.Schedule[1].Status.ShouldBe(ScheduleItemStatus.Unpaid);
    }

    [Fact]
    public void Apply_BeforeTheFirstDueDate_AllocatesNothing()
    {
        var result = Allocation.Apply(Car36, Sek(1_000m), new DateOnly(2026, 2, 14)).Value;

        result.Unallocated.ShouldBe(Sek(1_000m));
        result.Allocations.ShouldBeEmpty();
        result.Schedule.ShouldBe(Car36);
    }

    [Fact]
    public void Apply_PaidItemsAreSkipped()
    {
        var paid = Allocation.Apply(Car36, Sek(6_170.85m), Feb15).Value.Schedule;

        var result = Allocation.Apply(paid, Sek(500m), Mar15).Value;

        result.Allocations.ShouldHaveSingleItem().Number.ShouldBe(2);
    }

    [Fact]
    public void Apply_DoesNotChangeTheInput()
    {
        Allocation.Apply(Car36, Sek(6_170.85m), Feb15);

        Car36[0].PaidPrincipal.ShouldBe(Sek(0m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Apply_NonPositiveAmount_IsRejected(decimal amount) =>
        Allocation.Apply(Car36, Sek(amount), Feb15).Error.ShouldBe(LoanErrors.PaymentNotPositive);

    [Fact]
    public void Apply_AmountInAnotherCurrency_IsRejected() =>
        Allocation.Apply(Car36, new Money(100m, "EUR"), Feb15).Error.ShouldBe(LoanErrors.CurrencyMismatch);

    [Fact]
    public void Apply_MortgageProduct_PartialPaymentPaysInterestFirst()
    {
        // MORTGAGE item 1 due 15 Feb: interest 7,083.33, principal 2,755.47. 8,000 pays interest 7,083.33 and principal 8,000 − 7,083.33 = 916.67.
        var result = Allocation.Apply(Annuity(Mortgage()), Sek(8_000m), Feb15).Value;

        result.Allocations.ShouldHaveSingleItem().ShouldBe(new ItemAllocation(1, Sek(0m), Sek(7_083.33m), Sek(916.67m)));
        result.Schedule[0].UnpaidPrincipal.ShouldBe(Sek(1_838.80m));
    }

    [Fact]
    public void Apply_PurchaseProduct_PaysTheStageAndReturnsTheRest()
    {
        // PURCHASE stage 1 due 15 Apr: 25,000 principal + 2,095.89 interest = 27,095.89. 30,000 on 15 Apr pays it in full;
        // stage 2 (15 Jul) is not due, so 30,000 − 27,095.89 = 2,904.11 is returned.
        var terms = Purchase();
        var schedule = Amortization.BuildStaged(terms, new StagePlan([
            new Stage(new DateOnly(2026, 4, 15), Sek(25_000m)), new Stage(new DateOnly(2026, 7, 15), Sek(75_000m))])).Value;

        var result = Allocation.Apply(schedule, Sek(30_000m), new DateOnly(2026, 4, 15)).Value;

        result.Allocations.ShouldHaveSingleItem().ShouldBe(new ItemAllocation(1, Sek(0m), Sek(2_095.89m), Sek(25_000m)));
        result.Unallocated.ShouldBe(Sek(2_904.11m));
        result.Schedule[1].Status.ShouldBe(ScheduleItemStatus.Unpaid);
    }

    internal static IReadOnlyList<ScheduleItem> Replace(IReadOnlyList<ScheduleItem> schedule, int index, ScheduleItem item) =>
        [.. schedule.Select((existing, i) => i == index ? item : existing)];
}
