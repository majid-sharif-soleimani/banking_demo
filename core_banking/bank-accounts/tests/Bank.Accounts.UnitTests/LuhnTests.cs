using Bank.Accounts.Api.Logic;

namespace Bank.Accounts.UnitTests;

public class LuhnTests
{
    [Theory]
    // 7992739871 → check digit 3, the textbook example: from the right, double every second digit starting next to the check digit.
    [InlineData("7992739871", 3)]
    // 12345678: doubled from the right 8→16→7, 6→12→3, 4→8, 2→4; plus 7, 5, 3, 1 = 7+7+3+5+8+3+4+1 = 38 → (10 − 8) = 2
    [InlineData("12345678", 2)]
    [InlineData("10000001", 7)] // 1 doubled = 2, plus the leading 1 = 3 → 7
    [InlineData("99999999", 8)] // four doubled 9s (18 → 9) and four plain 9s = 72 → 8
    public void CheckDigit_KnownBodies_ReturnTheTextbookDigit(string body, int expected) =>
        Luhn.CheckDigit(body).ShouldBe(expected);

    [Theory]
    [InlineData("123456782")]
    [InlineData("100000017")]
    [InlineData("79927398713")]
    public void IsValid_NumberWithItsCheckDigit_IsTrue(string number) => Luhn.IsValid(number).ShouldBeTrue();

    [Theory]
    [InlineData("123456781")] // wrong check digit
    [InlineData("123456728")] // two neighbouring digits swapped (8 and 2): Luhn catches it
    [InlineData("223456782")] // one digit changed
    [InlineData("12345678a")]
    [InlineData("")]
    public void IsValid_TypoOrJunk_IsFalse(string number) => Luhn.IsValid(number).ShouldBeFalse();

    [Fact]
    public void AccountNumber_From8Digits_AppendsTheCheckDigit() =>
        AccountNumbers.Compose(12_345_678).ShouldBe("123456782");

    [Theory]
    [InlineData(9_999_999)]   // 7 digits: would lose its leading digit
    [InlineData(100_000_000)] // 9 digits
    public void AccountNumber_BodyOutsideEightDigits_Throws(int body) =>
        Should.Throw<ArgumentOutOfRangeException>(() => AccountNumbers.Compose(body));

    [Fact]
    public void AccountNumber_AnyComposedNumber_IsNineDigitsAndValid()
    {
        foreach (var body in new[] { 10_000_000, 54_321_987, 99_999_999 })
        {
            var number = AccountNumbers.Compose(body);
            number.Length.ShouldBe(9);
            Luhn.IsValid(number).ShouldBeTrue();
        }
    }
}

public class AmountScaleTests
{
    [Theory]
    [InlineData("250", 2, "250.00")]
    [InlineData("1000.5", 2, "1000.50")]
    [InlineData("250.00000000", 2, "250.00")]
    [InlineData("10.125", 2, "10.13")] // AwayFromZero, like MoneyRounding
    [InlineData("7", 0, "7")]
    public void Normalize_WritesExactlyTheCurrencyMinorUnits(string value, int minorUnits, string expected) =>
        AmountScale.Normalize(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), minorUnits)
            .ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe(expected);
}
