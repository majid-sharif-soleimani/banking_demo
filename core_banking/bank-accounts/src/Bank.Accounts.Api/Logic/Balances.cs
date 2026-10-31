namespace Bank.Accounts.Api.Logic;

/// <summary>One entry as far as balances are concerned: its business date, its posting order, the balance after it.</summary>
public sealed record BalancePoint(DateOnly BusinessDate, long Sequence, decimal BalanceAfter);

public static class Balances
{
    /// <summary>
    /// The balance at the end of <paramref name="date"/>: the balance after the last entry (highest sequence) whose
    /// business date is on or before it; zero before the first entry. Read from entries alone (design plan section 2).
    /// </summary>
    public static decimal AtDate(IEnumerable<BalancePoint> entries, DateOnly date) =>
        entries.Where(e => e.BusinessDate <= date).MaxBy(e => e.Sequence)?.BalanceAfter ?? 0m;
}
