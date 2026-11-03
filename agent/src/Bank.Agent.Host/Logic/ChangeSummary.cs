using System.Text;

namespace Bank.Agent.Host.Logic;

/// <summary>One line of a confirmation card: what the label says and the value that will be sent.</summary>
public sealed record SummaryField(string Label, string Value);

/// <summary>
/// What a confirmation card says (phase 2 design plan, flow 4.3): a title for the change and the exact values the tool
/// will send. The values come from the tool call's arguments, not from the model's description of them, so what the
/// person confirms is what the bank receives.
/// </summary>
public static class ChangeSummary
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["transfer_create"] = "Transfer money",
        ["standing_order_create"] = "Create a standing order",
        ["standing_order_cancel"] = "Cancel a standing order",
        ["card_payment"] = "Pay a loan installment by card",
        ["payoff_request"] = "Pay off a loan early",
        ["appointment_book"] = "Book an appointment",
        ["appointment_cancel"] = "Cancel an appointment",
        ["loan_application_submit"] = "Submit a loan application",
        ["currency_activate"] = "Activate a currency",
        ["business_day_close"] = "Close the business day",
        ["loan_application_start_review"] = "Start reviewing a loan application",
        ["loan_application_approve"] = "Approve a loan application",
        ["loan_application_reject"] = "Reject a loan application",
        ["loan_application_disburse"] = "Pay out a loan",
        ["loan_application_request_information"] = "Ask the customer for documents",
        ["information_request_complete"] = "Mark an information request as completed",
    };

    public static string Title(string tool) => Titles.GetValueOrDefault(tool, Label(tool));

    /// <summary>The arguments as card lines, in the order the model sent them; empty values are left out.</summary>
    public static IReadOnlyList<SummaryField> Fields(IEnumerable<KeyValuePair<string, string?>> arguments) =>
        [.. arguments.Where(a => !string.IsNullOrWhiteSpace(a.Value)).Select(a => new SummaryField(Label(a.Key), a.Value!))];

    /// <summary>"fromAccountId" → "From account id", "standing_order_cancel" → "Standing order cancel".</summary>
    public static string Label(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name)
        {
            if (c == '_')
            {
                text.Append(' ');
            }
            else if (char.IsUpper(c) && text.Length > 0)
            {
                text.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : c);
            }
        }

        return text.ToString();
    }
}
