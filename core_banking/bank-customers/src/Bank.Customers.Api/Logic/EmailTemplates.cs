using System.Globalization;
using Bank.Common;
using Bank.Contracts;

namespace Bank.Customers.Api.Logic;

/// <summary>The bank's customer emails, in English (stage 4). Amounts and dates are formatted the same in every culture.</summary>
public static class EmailTemplates
{
    public const string StandingOrderFailedTemplate = "standing_order_failed";

    public static EmailMessage StandingOrderFailed(string to, string name, Money amount, DateOnly runDate, string reason = "insufficient_funds") => new(
        to,
        "Your standing order could not be paid",
        $"""
        Dear {name},

        Your standing order of {Format(amount)} due on {runDate:yyyy-MM-dd} could not be paid, {Because(reason)}.

        The payment will not be retried. The standing order stays active and runs again on its next date.

        Kind regards,
        Your bank
        """);

    public const string InformationRequestedTemplate = "information_requested";
    public const string PaymentFailedTemplate = "payment_failed";
    public const string ClassificationWarningTemplate = "classification_warning";
    public const string PaidOffEarlyTemplate = "paid_off_early";

    public static EmailMessage InformationRequested(string to, string name, string items) => new(
        to,
        "We need more information for your loan application",
        $"""
        Dear {name},

        To continue reviewing your loan application we need the following:

        {string.Join('\n', items.Split('\n').Select(i => $"- {i}"))}

        You can send the documents to us or reply to this email.

        Kind regards,
        Your bank
        """);

    public static EmailMessage PaymentFailed(string to, string name, Money amount, string purpose) => new(
        to,
        "A loan payment could not be collected",
        $"""
        Dear {name},

        We could not collect your loan {(purpose == "Payoff" ? "early payoff" : "installment")} of {Format(amount)}, because the account did not have enough money.

        {(purpose == "Payoff" ? "Your loan continues as before." : "We will try again on the next business day. Late payments are charged a penalty after the grace period.")}

        Kind regards,
        Your bank
        """);

    public static EmailMessage ClassificationWarning(string to, string name, string classification, int daysPastDue) => new(
        to,
        "Your loan payments are late",
        $"""
        Dear {name},

        A payment on your loan is {daysPastDue} days late, and the loan is now classified as {classification}.

        Please make sure the repayment account has enough money, or contact us to discuss a solution.

        Kind regards,
        Your bank
        """);

    public static EmailMessage PaidOffEarly(string to, string name, Money amount, Money waived) => new(
        to,
        "Your loan is paid off",
        $"""
        Dear {name},

        Thank you. Your loan was paid off early with a final payment of {Format(amount)}. Future interest of {Format(waived)} was waived.

        Kind regards,
        Your bank
        """);

    private static string Because(string reason) => reason switch
    {
        "insufficient_funds" => "because the account did not have enough money",
        "fx_unavailable" => "because no exchange rate was available",
        _ => "because of a problem on our side",
    };

    private static string Format(Money money) => $"{money.Amount.ToString("N2", CultureInfo.InvariantCulture)} {money.Currency}";
}
