namespace Bank.Accounts.Api.Logic;

/// <summary>
/// Gives an amount exactly the currency's number of decimals (250 → 250.00 for SEK), rounding AwayFromZero like
/// MoneyRounding. Amounts are stored in unconstrained numeric columns, which keep the scale written, so every amount in
/// the database, the API and the events looks the same.
/// </summary>
public static class AmountScale
{
    public static decimal Normalize(decimal amount, int minorUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minorUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minorUnits, 28);
        // Adding a zero with the target scale raises the scale; rounding then lowers anything finer.
        return Math.Round(amount + new decimal(0, 0, 0, false, (byte)minorUnits), minorUnits, MidpointRounding.AwayFromZero);
    }
}
