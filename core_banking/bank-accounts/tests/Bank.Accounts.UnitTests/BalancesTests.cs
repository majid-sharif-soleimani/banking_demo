using Bank.Accounts.Api.Logic;

namespace Bank.Accounts.UnitTests;

public class BalancesTests
{
    private static readonly DateOnly Mar1 = new(2026, 3, 1);

    // Mar 1: +100 (100), +50 (150); Mar 3: −30 (120); Mar 5: +10 (130)
    private static readonly IReadOnlyList<BalancePoint> Entries =
    [
        new(Mar1, 1, 100m), new(Mar1, 2, 150m), new(Mar1.AddDays(2), 3, 120m), new(Mar1.AddDays(4), 4, 130m),
    ];

    [Fact]
    public void AtDate_BeforeTheFirstEntry_IsZero() => Balances.AtDate(Entries, Mar1.AddDays(-1)).ShouldBe(0m);

    [Fact]
    public void AtDate_DayWithSeveralEntries_IsTheBalanceAfterTheLastOne() => Balances.AtDate(Entries, Mar1).ShouldBe(150m);

    [Fact]
    public void AtDate_DayWithoutEntries_KeepsThePreviousBalance() => Balances.AtDate(Entries, Mar1.AddDays(1)).ShouldBe(150m);

    [Fact]
    public void AtDate_AfterTheLastEntry_IsTheLatestBalance() => Balances.AtDate(Entries, Mar1.AddDays(30)).ShouldBe(130m);

    [Fact]
    public void AtDate_EntriesInAnyOrder_UsesTheSequence()
    {
        // Two entries on one date: the higher sequence was posted later, whatever order they are given in.
        IReadOnlyList<BalancePoint> shuffled = [new(Mar1, 2, 150m), new(Mar1, 1, 100m)];

        Balances.AtDate(shuffled, Mar1).ShouldBe(150m);
    }

    [Fact]
    public void AtDate_NoEntries_IsZero() => Balances.AtDate([], Mar1).ShouldBe(0m);
}
