using Bank.Agent.Host.Features.AgUi;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// Stands in front of a bank read whose result holds text written by customers (notes, email replies, file names, the
/// purpose of a loan application). The model gets the same result, but between <see cref="UntrustedContent"/> markers,
/// so it can tell the customer's words from its instructions.
/// </summary>
public sealed class MarkUntrusted(AIFunction inner) : DelegatingAIFunction(inner)
{
    /// <summary>The bank reads that carry customer-written text.</summary>
    public static readonly string[] Tools =
    [
        "customer_interactions", "loan_application_get", "loan_applications_queue", "loan_applications_list",
        "information_requests_open", "information_requests_of_application",
    ];

    public static AIFunction Around(AIFunction tool) => Tools.Contains(tool.Name) ? new MarkUntrusted(tool) : tool;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var outcome = ToolResult.ToOutcome(await base.InvokeCoreAsync(arguments, cancellationToken));
        return outcome.Status == "done"
            ? UntrustedContent.Wrap(Name, outcome.Message ?? "")
            : $"The bank refused the call: {outcome.Message}";
    }
}
