using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// Stands in front of a bank read whose result may hold text written outside the bank (names, addresses, notes, email
/// replies, file names, the purpose of a loan application, a transfer's message). The model gets the same result, but
/// between <see cref="UntrustedContent"/> markers, so it can tell those words from its instructions.
///
/// The default is safe: every read is marked, except the short list of reads whose result only the bank wrote. A new
/// read tool is marked until someone decides otherwise; forgetting this list costs a few extra tokens, not a hole.
/// </summary>
public sealed class MarkUntrusted(AIFunction inner) : DelegatingAIFunction(inner)
{
    /// <summary>The reads whose result the bank alone wrote: dates, products, rates, slots, totals.</summary>
    public static readonly string[] BankOnlyReads =
    [
        "business_date", "loan_products_list", "appointment_slots", "loan_simulation", "payoff_quote",
        "currencies_list", "trial_balance", "portfolio_report",
    ];

    public static AIFunction Around(AIFunction tool) => BankOnlyReads.Contains(tool.Name) ? tool : new MarkUntrusted(tool);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var answer = ToolResult.Read(await base.InvokeCoreAsync(arguments, cancellationToken));
        return answer.Done
            ? UntrustedContent.Wrap(Name, answer.Text ?? "")
            : $"The bank refused the call: {answer.Text}";
    }
}
