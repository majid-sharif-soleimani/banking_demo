using Bank.Contracts;

namespace Bank.Common;

/// <summary>
/// The only place money is rounded. Money itself never rounds, so callers decide when (for example per installment,
/// with the leftover going into the last one). The minor units come from the currency (CurrencyActivated.MinorUnits).
/// </summary>
public static class MoneyRounding
{
    // decimal holds at most 28 digits after the decimal point.
    private const int MaxMinorUnits = 28;

    public static Money Round(Money money, int minorUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minorUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minorUnits, MaxMinorUnits);

        return money with { Amount = Math.Round(money.Amount, minorUnits, MidpointRounding.AwayFromZero) };
    }
}
