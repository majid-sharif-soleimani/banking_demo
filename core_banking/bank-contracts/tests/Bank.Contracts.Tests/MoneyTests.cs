namespace Bank.Contracts.Tests;

public class MoneyTests
{
    private static Money Sek(decimal amount) => new(amount, "SEK");
    private static Money Eur(decimal amount) => new(amount, "EUR");

    [Fact]
    public void Adding_two_amounts_in_the_same_currency_sums_them()
    {
        // 100.25 + 0.75 = 101.00
        (Sek(100.25m) + Sek(0.75m)).ShouldBe(Sek(101.00m));
    }

    [Fact]
    public void Subtracting_can_go_below_zero()
    {
        // 10 - 25 = -15
        (Sek(10m) - Sek(25m)).ShouldBe(Sek(-15m));
    }

    [Fact]
    public void Multiplying_by_a_decimal_keeps_full_precision()
    {
        // 1000 * 0.0695 / 12 = 5.791666...; Money never rounds on its own
        var monthlyInterest = Sek(1000m) * (0.0695m / 12m);

        monthlyInterest.Amount.ShouldBe(1000m * (0.0695m / 12m));
        monthlyInterest.Currency.ShouldBe("SEK");
    }

    [Fact]
    public void Multiplication_is_commutative()
    {
        (2m * Sek(7.5m)).ShouldBe(Sek(7.5m) * 2m);
    }

    [Fact]
    public void Equal_amounts_with_different_scale_are_equal()
    {
        // 10.0 and 10.00 are the same amount of money
        Sek(10.0m).ShouldBe(Sek(10.00m));
        Sek(10.0m).GetHashCode().ShouldBe(Sek(10.00m).GetHashCode());
    }

    [Fact]
    public void Comparison_operators_compare_amounts()
    {
        (Sek(1m) < Sek(2m)).ShouldBeTrue();
        (Sek(2m) > Sek(1m)).ShouldBeTrue();
        (Sek(2m) <= Sek(2.00m)).ShouldBeTrue();
        (Sek(2m) >= Sek(3m)).ShouldBeFalse();
    }

    [Fact]
    public void Comparison_operators_treat_null_as_less_than_any_amount()
    {
        // Same order as CompareTo(null), which returns 1
        Money? none = null;

        (none < Sek(1m)).ShouldBeTrue();
        (none > Sek(1m)).ShouldBeFalse();
        (Sek(1m) > none).ShouldBeTrue();
        (Sek(1m) >= none).ShouldBeTrue();
    }

    [Fact]
    public void Zero_creates_an_empty_amount_in_the_currency()
    {
        Money.Zero("EUR").ShouldBe(Eur(0m));
    }

    [Fact]
    public void Adding_different_currencies_throws()
    {
        var ex = Should.Throw<CurrencyMismatchException>(() => Sek(1m) + Eur(1m));

        ex.Left.ShouldBe("SEK");
        ex.Right.ShouldBe("EUR");
    }

    [Fact]
    public void Subtracting_different_currencies_throws()
    {
        Should.Throw<CurrencyMismatchException>(() => Sek(1m) - Eur(1m));
    }

    [Fact]
    public void Comparing_different_currencies_throws()
    {
        Should.Throw<CurrencyMismatchException>(() => Sek(1m) < Eur(1m));
        Should.Throw<CurrencyMismatchException>(() => Sek(1m).CompareTo(Eur(1m)));
    }

    [Fact]
    public void Different_currencies_are_not_equal()
    {
        // Equality never throws; it is simply false
        Sek(1m).ShouldNotBe(Eur(1m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SE")]
    [InlineData("sek")]
    [InlineData("SEKK")]
    [InlineData("S3K")]
    public void Currency_must_be_a_three_letter_upper_case_code(string currency)
    {
        Should.Throw<ArgumentException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Currency_cannot_be_null()
    {
        Should.Throw<ArgumentException>(() => new Money(1m, null!));
    }
}
