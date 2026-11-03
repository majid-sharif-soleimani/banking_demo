using Bank.Accounts.Api.Logic;
using Bank.Contracts;

namespace Bank.Accounts.UnitTests;

public class TransactionSearchTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static TransactionFilter Filter(
        DateOnly? from = null, DateOnly? to = null, Money? min = null, Money? max = null, string? currency = null) =>
        new(from, to, min, max, currency);

    [Fact]
    public void Validate_NoFilter_IsValid() => TransactionSearch.Validate(Filter(), Today).ShouldBeNull();

    [Fact]
    public void Validate_FullFilter_IsValid() =>
        TransactionSearch.Validate(Filter(Today.AddDays(-7), Today, new(100m, "SEK"), new(1_000m, "SEK"), "SEK"), Today).ShouldBeNull();

    [Fact]
    public void Validate_FromAfterTo_IsInvalidPeriod() =>
        TransactionSearch.Validate(Filter(Today, Today.AddDays(-1)), Today)!.Code.ShouldBe("invalid_period");

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 1)]
    public void Validate_DateAfterToday_IsDateInFuture(int? fromDays, int? toDays) =>
        TransactionSearch.Validate(Filter(fromDays is { } f ? Today.AddDays(f) : null, toDays is { } t ? Today.AddDays(t) : null), Today)!
            .Code.ShouldBe("date_in_future");

    [Fact]
    public void Validate_NegativeAmount_IsInvalidAmountRange() =>
        TransactionSearch.Validate(Filter(min: new(-1m, "SEK")), Today)!.Code.ShouldBe("invalid_amount_range");

    [Fact]
    public void Validate_MinAboveMax_IsInvalidAmountRange() =>
        TransactionSearch.Validate(Filter(min: new(500m, "SEK"), max: new(100m, "SEK")), Today)!.Code.ShouldBe("invalid_amount_range");

    [Theory]
    [InlineData("EUR", "SEK", null)]
    [InlineData("SEK", null, "EUR")]
    [InlineData(null, "SEK", "EUR")]
    public void Validate_AmountsInAnotherCurrency_IsCurrencyMismatch(string? minCurrency, string? maxCurrency, string? currency) =>
        TransactionSearch.Validate(Filter(
            min: minCurrency is null ? null : new(1m, minCurrency),
            max: maxCurrency is null ? null : new(1_000m, maxCurrency),
            currency: currency), Today)!.Code.ShouldBe("currency_mismatch");

    [Fact]
    public void Currency_ComesFromTheFilterOrFromTheAmounts()
    {
        TransactionSearch.Currency(Filter(currency: "EUR")).ShouldBe("EUR");
        TransactionSearch.Currency(Filter(max: new(10m, "USD"))).ShouldBe("USD");
        TransactionSearch.Currency(Filter()).ShouldBeNull();
    }
}
