using System.Globalization;
using Bank.Common;
using Bank.Contracts;

namespace Bank.Customers.Api.Logic;

/// <summary>The bank's customer emails, in English (stage 4). Amounts and dates are formatted the same in every culture.</summary>
public static class EmailTemplates
{
    public const string StandingOrderFailedTemplate = "standing_order_failed";

    public static EmailMessage StandingOrderFailed(string to, string name, Money amount, DateOnly runDate) => new(
        to,
        "Your standing order could not be paid",
        $"""
        Dear {name},

        Your standing order of {Format(amount)} due on {runDate:yyyy-MM-dd} could not be paid, because the account did not have enough money.

        The payment will not be retried. The standing order stays active and runs again on its next date.

        Kind regards,
        Your bank
        """);

    private static string Format(Money money) => $"{money.Amount.ToString("N2", CultureInfo.InvariantCulture)} {money.Currency}";
}
