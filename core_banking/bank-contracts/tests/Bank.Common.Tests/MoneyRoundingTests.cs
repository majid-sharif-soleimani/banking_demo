using Bank.Contracts;

namespace Bank.Common.Tests;

public class MoneyRoundingTests
{
    [Theory]
    [InlineData("1.005", 2, "1.01")]    // midpoint goes away from zero, not to even (banker's rounding would give 1.00)
    [InlineData("-1.005", 2, "-1.01")]  // away from zero on the negative side too
    [InlineData("1.004", 2, "1.00")]
    [InlineData("2.5", 0, "3")]         // a currency with no minor units, such as JPY
    [InlineData("12.3456", 3, "12.346")]
    public void Round_uses_the_minor_units_and_rounds_midpoints_away_from_zero(string amount, int minorUnits, string expected)
    {
        var rounded = MoneyRounding.Round(new Money(decimal.Parse(amount), "SEK"), minorUnits);

        rounded.ShouldBe(new Money(decimal.Parse(expected), "SEK"));
    }

    [Fact]
    public void Round_keeps_the_currency()
    {
        MoneyRounding.Round(new Money(10.125m, "EUR"), 2).Currency.ShouldBe("EUR");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Round_rejects_minor_units_outside_what_decimal_can_hold(int minorUnits)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MoneyRounding.Round(new Money(1m, "SEK"), minorUnits));
    }
}
