using Bank.Lending.Api.Logic;
using static Bank.Lending.UnitTests.Support.Loans;

namespace Bank.Lending.UnitTests;

public class ClassificationTests
{
    // CAR item 1 due 15 Feb 2026; overdue after 1 day, default after 90 days.
    private static readonly LoanTerms CarTerms = Car();
    private static readonly IReadOnlyList<ScheduleItem> Car36 = Annuity(CarTerms);
    private static readonly DateOnly Feb15 = new(2026, 2, 15);

    [Fact]
    public void For_BeforeTheFirstDueDate_IsCurrent() =>
        Classification.For(Car36, CarTerms, new DateOnly(2026, 2, 1)).ShouldBe(LoanClassification.Current);

    [Fact]
    public void For_OnTheDueDateUnpaid_IsCurrent() =>
        // 0 days past due: the installment is payable today, not late yet.
        Classification.For(Car36, CarTerms, Feb15).ShouldBe(LoanClassification.Current);

    [Fact]
    public void For_ExactlyOneDayPastDue_IsOverdue() =>
        Classification.For(Car36, CarTerms, Feb15.AddDays(1)).ShouldBe(LoanClassification.Overdue);

    [Fact]
    public void For_89DaysPastDue_IsStillOverdue() =>
        Classification.For(Car36, CarTerms, Feb15.AddDays(89)).ShouldBe(LoanClassification.Overdue);

    [Fact]
    public void For_Exactly90DaysPastDue_IsDefault() =>
        Classification.For(Car36, CarTerms, Feb15.AddDays(90)).ShouldBe(LoanClassification.Default);

    [Fact]
    public void For_OldestUnpaidItemDecides()
    {
        // Item 1 paid; item 2 (due 15 Mar) unpaid. On 20 Apr: 36 days past due on item 2 → Overdue, not counted from item 1.
        var paid = Allocation.Apply(Car36, CarTerms, Sek(6_170.85m), Feb15).Value.Schedule;

        Classification.For(paid, CarTerms, new DateOnly(2026, 4, 20)).ShouldBe(LoanClassification.Overdue);
        Classification.For(paid, CarTerms, new DateOnly(2026, 3, 15)).ShouldBe(LoanClassification.Current);
    }

    [Fact]
    public void For_EverythingDuePaid_IsCurrent()
    {
        var paid = Allocation.Apply(Car36, CarTerms, Sek(6_170.85m), Feb15).Value.Schedule;

        Classification.For(paid, CarTerms, new DateOnly(2026, 3, 1)).ShouldBe(LoanClassification.Current);
    }

    [Fact]
    public void For_OnlyPenaltyUnpaid_IsStillOverdue()
    {
        // ADR 0003: an item is unpaid while any part, penalty included, is unpaid.
        var onlyPenalty = Car36[0] with { PenaltyDue = Sek(3.09m), PaidInterest = Sek(1_158.33m), PaidPrincipal = Sek(5_012.52m) };

        Classification.For(AllocationTests.Replace(Car36, 0, onlyPenalty), CarTerms, new DateOnly(2026, 3, 1))
            .ShouldBe(LoanClassification.Overdue);
    }

    [Fact]
    public void For_ThresholdsComeFromTheTerms()
    {
        var strict = CarTerms with { OverdueAfterDays = 10, DefaultAfterDays = 30 };

        Classification.For(Car36, strict, Feb15.AddDays(9)).ShouldBe(LoanClassification.Current);
        Classification.For(Car36, strict, Feb15.AddDays(10)).ShouldBe(LoanClassification.Overdue);
        Classification.For(Car36, strict, Feb15.AddDays(30)).ShouldBe(LoanClassification.Default);
    }

    [Fact]
    public void For_MortgageProduct_DefaultsAfterExactly90Days()
    {
        // Item 1 due 15 Feb 2026. 15 Feb → 15 May = 28 + 31 + 30 = 89 days → Overdue; 16 May = 90 days → Default.
        var terms = Mortgage();
        var schedule = Annuity(terms);

        Classification.For(schedule, terms, new DateOnly(2026, 5, 15)).ShouldBe(LoanClassification.Overdue);
        Classification.For(schedule, terms, new DateOnly(2026, 5, 16)).ShouldBe(LoanClassification.Default);
    }

    [Fact]
    public void For_PurchaseProduct_CountsFromTheStageDueDate()
    {
        // Stage 1 due 15 Apr 2026. 16 Apr = 1 day → Overdue; 15 Apr → 14 Jul = 30 + 31 + 29 = 90 days → Default.
        var terms = Purchase();
        var schedule = Amortization.BuildStaged(terms, new StagePlan([
            new Stage(new DateOnly(2026, 4, 15), Sek(25_000m)), new Stage(new DateOnly(2026, 10, 15), Sek(75_000m))])).Value;

        Classification.For(schedule, terms, new DateOnly(2026, 4, 15)).ShouldBe(LoanClassification.Current);
        Classification.For(schedule, terms, new DateOnly(2026, 4, 16)).ShouldBe(LoanClassification.Overdue);
        Classification.For(schedule, terms, new DateOnly(2026, 7, 13)).ShouldBe(LoanClassification.Overdue);
        Classification.For(schedule, terms, new DateOnly(2026, 7, 14)).ShouldBe(LoanClassification.Default);
    }
}
