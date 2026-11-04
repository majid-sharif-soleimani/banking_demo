using Bank.Agent.Host.Features.Auth;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// The tools of one run, in one place, so the chat and the evaluation set (agent/eval) give the model exactly the same
/// tools: the role's bank reads (all but the bank-only ones between untrusted markers), every change behind
/// <see cref="ConfirmFirst"/>, the UI tools, and for the admin the isolated document reader and the customers report.
/// </summary>
public static class AgentTools
{
    /// <param name="reader">The model of the document reader (<see cref="ReaderModel"/>), not the agent's.</param>
    public static IList<AITool> For(BankToolSet bank, HostSession session, UiTools ui, IChatClient reader)
    {
        IList<AITool> tools =
        [
            .. bank.Reads.Select(MarkUntrusted.Around),
            .. bank.Changes.Select(c => new ConfirmFirst(c, session, ui)),
            .. ui.Tools,
        ];
        if (bank.Find(McpBankTools.CustomerDocument) is { } documentFile)
        {
            tools.Add(new DocumentReader(reader, documentFile).Tool);
        }

        if (bank.Find(McpBankTools.CustomersReport) is { } report)
        {
            tools.Add(new CustomersReportTool(report, ui).Tool);
        }

        return tools;
    }
}
