using Bank.Accounts.Api.Logic;
using Bank.Contracts;

namespace Bank.Accounts.UnitTests;

public class FxTests
{
    [Fact]
    public void Convert_EurToSek_MultipliesByTheRate() =>
        // 100 EUR × 11.50 SEK/EUR = 1,150.00 SEK
        Fx.Convert(new Money(100m, "EUR"), 11.50m, "SEK", 2).ShouldBe(new Money(1_150.00m, "SEK"));

    [Fact]
    public void Convert_SekToEur_RoundsToTheTargetMinorUnits() =>
        // 100 SEK × (1 / 11.50) = 8.695652… → 8.70 EUR
        Fx.Convert(new Money(100m, "SEK"), Fx.Invert(11.50m), "EUR", 2).ShouldBe(new Money(8.70m, "EUR"));

    [Fact]
    public void Convert_MidpointRoundsAwayFromZero() =>
        // 1.00 × 1.005 = 1.005 → 1.01 (AwayFromZero, never banker's rounding to 1.00)
        Fx.Convert(new Money(1m, "SEK"), 1.005m, "EUR", 2).ShouldBe(new Money(1.01m, "EUR"));

    [Fact]
    public void Convert_ResultHasExactlyTheTargetDecimals() =>
        Fx.Convert(new Money(10m, "EUR"), 11.5m, "SEK", 2).Amount.ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe("115.00");

    [Fact]
    public void Convert_ZeroDecimalCurrency_RoundsToWholeUnits() =>
        // 10 SEK × 14.237 = 142.37 → 142 in a currency without minor units
        Fx.Convert(new Money(10m, "SEK"), 14.237m, "JPY", 0).ShouldBe(new Money(142m, "JPY"));

    [Fact]
    public void Cross_EurToUsdThroughSek_DividesTheSekQuotes() =>
        // 1 EUR = 11.50 SEK, 1 USD = 10.60 SEK → 1 EUR = 11.50 / 10.60 = 1.0849056604 USD (10 decimals)
        Fx.Cross(11.50m, 10.60m).ShouldBe(1.0849056604m);

    [Fact]
    public void Invert_KeepsTenDecimals() => Fx.Invert(11.50m).ShouldBe(0.0869565217m);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Convert_NonPositiveRate_Throws(decimal rate) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Fx.Convert(new Money(1m, "SEK"), rate, "EUR", 2));
}
