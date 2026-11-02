using Bank.Contracts;
using Bank.Customers.Api.Logic;

namespace Bank.Customers.UnitTests;

public class EmailTemplatesTests
{
    [Fact]
    public void StandingOrderFailed_FillsTheEnglishTemplate()
    {
        var message = EmailTemplates.StandingOrderFailed("ada@example.com", "Ada Lovelace", new Money(1_500m, "SEK"), new DateOnly(2026, 3, 1));

        message.To.ShouldBe("ada@example.com");
        message.Subject.ShouldBe("Your standing order could not be paid");
        message.Body.ShouldContain("Dear Ada Lovelace,");
        message.Body.ShouldContain("1,500.00 SEK");
        message.Body.ShouldContain("2026-03-01");
        message.Body.ShouldContain("will not be retried");
    }

    [Fact]
    public void StandingOrderFailed_FormatsAmountsTheSameInEveryCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");

            EmailTemplates.StandingOrderFailed("a@b.se", "A", new Money(1_234.5m, "EUR"), new DateOnly(2026, 3, 1)).Body
                .ShouldContain("1,234.50 EUR");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("insufficient_funds", "did not have enough money")]
    [InlineData("fx_unavailable", "no exchange rate was available")]
    [InlineData("technical_error", "a problem on our side")]
    public void StandingOrderFailed_ExplainsTheReason(string reason, string expected) =>
        EmailTemplates.StandingOrderFailed("a@b.se", "A", new Money(1m, "SEK"), new DateOnly(2026, 3, 1), reason).Body.ShouldContain(expected);
}
