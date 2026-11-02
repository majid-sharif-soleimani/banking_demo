using Bank.Contracts;

namespace Bank.Accounts.Api.Logic;

/// <summary>Currency conversion for transfers (design plan section 2). Rates are units of target per unit of source.</summary>
public static class Fx
{
    /// <summary>Decimals kept for a derived (inverted or crossed) rate; quoted rates are used as published.</summary>
    public const int RateDecimals = 10;

    /// <summary>money × rate in <paramref name="targetCurrency"/>, rounded AwayFromZero to its minor units.</summary>
    public static Money Convert(Money money, decimal rate, string targetCurrency, int minorUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);
        return new Money(AmountScale.Normalize(money.Amount * rate, minorUnits), targetCurrency);
    }

    /// <summary>The opposite direction of a rate: 1 / rate.</summary>
    public static decimal Invert(decimal rate) => Math.Round(1m / rate, RateDecimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The rate from currency A to B when both are quoted against SEK (SEK per unit, as the Riksbank publishes them):
    /// A → B = (SEK per A) / (SEK per B).
    /// </summary>
    public static decimal Cross(decimal sekPerFrom, decimal sekPerTo) =>
        Math.Round(sekPerFrom / sekPerTo, RateDecimals, MidpointRounding.AwayFromZero);
}
