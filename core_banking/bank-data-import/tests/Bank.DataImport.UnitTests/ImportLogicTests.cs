using Bank.Contracts;
using Bank.DataImport.Logic;

namespace Bank.DataImport.UnitTests;

public class SelectionTests
{
    private static Candidate C(int id, bool loan = false, bool late = false, bool order = false) => new($"c{id}", loan, late, order);

    [Fact]
    public void Select_MeetsTheMinimumsAndFillsTheRestRandomly()
    {
        // 500 candidates: 60 with loans (20 of them late), 40 with standing orders, the rest plain.
        var candidates = Enumerable.Range(0, 500).Select(i => C(i, loan: i < 60, late: i < 20, order: i is >= 60 and < 100)).ToList();

        var selected = Selection.Select(candidates, new SelectionTargets(100, 30, 10, 15), seed: 42);

        selected.Count.ShouldBe(100);
        selected.Distinct().Count().ShouldBe(100);
        var chosen = candidates.Where(c => selected.Contains(c.Id)).ToList();
        chosen.Count(c => c.HasLatePayments).ShouldBeGreaterThanOrEqualTo(10);
        chosen.Count(c => c.HasLoan).ShouldBeGreaterThanOrEqualTo(30);
        chosen.Count(c => c.HasStandingOrder).ShouldBeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public void Select_SameSeed_SameCustomers() =>
        Selection.Select(Enumerable.Range(0, 300).Select(i => C(i, loan: i % 3 == 0)).ToList(), new SelectionTargets(50, 10, 0, 0), 7)
            .ShouldBe(Selection.Select(Enumerable.Range(0, 300).Select(i => C(i, loan: i % 3 == 0)).ToList(), new SelectionTargets(50, 10, 0, 0), 7));

    [Fact]
    public void Select_FewerCandidatesThanWanted_TakesAll() =>
        Selection.Select([C(1, loan: true), C(2)], new SelectionTargets(100, 30, 10, 15), 1).Count.ShouldBe(2);
}

public class DateShiftTests
{
    [Fact]
    public void Shift_MovesTheFirstKeptEventToTheStartDate()
    {
        // History 2019-03-10 … 2020-06-30 (under two years): the first event lands on 2026-01-01, the gap is kept.
        var shift = DateShift.Compute(new DateOnly(2019, 3, 10), new DateOnly(2020, 6, 30), new DateOnly(2026, 1, 1), maxYears: 2);

        shift.Apply(new DateOnly(2019, 3, 10)).ShouldBe(new DateOnly(2026, 1, 1));
        shift.Apply(new DateOnly(2019, 3, 11)).ShouldBe(new DateOnly(2026, 1, 2));
        shift.Keeps(new DateOnly(2019, 3, 10)).ShouldBeTrue();
    }

    [Fact]
    public void Shift_LongHistory_KeepsOnlyTheLastTwoYears()
    {
        // 2015 … 2020-06-30: only events from 2018-06-30 on are kept; 2018-06-30 becomes the start date.
        var shift = DateShift.Compute(new DateOnly(2015, 1, 1), new DateOnly(2020, 6, 30), new DateOnly(2026, 1, 1), maxYears: 2);

        shift.Keeps(new DateOnly(2018, 6, 29)).ShouldBeFalse();
        shift.Keeps(new DateOnly(2018, 6, 30)).ShouldBeTrue();
        shift.Apply(new DateOnly(2018, 6, 30)).ShouldBe(new DateOnly(2026, 1, 1));
        shift.Apply(new DateOnly(2020, 6, 30)).ShouldBe(new DateOnly(2028, 1, 2)); // 731 days later (2020 is a leap year)
    }
}

public class ConversionAndMappingTests
{
    [Theory]
    [InlineData("123.45", "10.0", "1234.50")]
    [InlineData("0.125", "10.0", "1.25")]
    [InlineData("1.005", "1.0", "1.01")] // AwayFromZero
    public void ToSek_MultipliesByTheFactorAndRoundsToOre(string amount, string factor, string expected) =>
        Amounts.ToSek(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(factor, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(new Money(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), "SEK"));

    [Theory]
    [InlineData("Car loan", "CAR")]
    [InlineData("AUTO financing", "CAR")]
    [InlineData("Vehicle", "CAR")]
    [InlineData("Home mortgage", "MORTGAGE")]
    [InlineData("House purchase", "MORTGAGE")]
    [InlineData("Personal", "PURCHASE")]
    [InlineData("", "PURCHASE")]
    public void LoanType_MapsByKeyword(string text, string product) => LoanTypes.Map(text).ShouldBe(product);

    [Fact]
    public void IdempotencyKey_IsStablePerSourceRowAndDiffersBetweenRows()
    {
        var a = IdempotencyKeys.For("transactions", "t1", "deposit");
        a.ShouldBe(IdempotencyKeys.For("transactions", "t1", "deposit"));
        a.ShouldNotBe(IdempotencyKeys.For("transactions", "t2", "deposit"));
        a.ShouldNotBe(IdempotencyKeys.For("loan_payments", "t1", "deposit"));
        a.Length.ShouldBe(40);
    }
}

public class StagePlannerTests
{
    [Fact]
    public void Equal_SplitsThePrincipalIntoQuarterlyStagesEndingAtMaturity()
    {
        // 100,000 over 12 months, at most 4 stages: every 3 months, 25,000 each, the last on 15 Jan 2027.
        var stages = StagePlanner.Equal(new Money(100_000m, "SEK"), new DateOnly(2026, 1, 15), 12, 4);

        stages.Select(s => s.DueDate).ShouldBe([new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 15), new DateOnly(2026, 10, 15), new DateOnly(2027, 1, 15)]);
        stages.ShouldAllBe(s => s.Share.Amount == 25_000m);
    }

    [Fact]
    public void Equal_ShortTermOrOddAmount_PutsTheRemainderLast()
    {
        // 10,000.01 over 3 months: one stage per quarter at most → 1 stage of the whole amount.
        StagePlanner.Equal(new Money(10_000.01m, "SEK"), new DateOnly(2026, 1, 15), 3, 4).ShouldHaveSingleItem().Share.Amount.ShouldBe(10_000.01m);
        // 100,000 over 9 months: 3 stages, 33,333.33 + 33,333.33 + 33,333.34.
        StagePlanner.Equal(new Money(100_000m, "SEK"), new DateOnly(2026, 1, 15), 9, 4).Select(s => s.Share.Amount).ShouldBe([33_333.33m, 33_333.33m, 33_333.34m]);
    }
}
