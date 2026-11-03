using Bank.Agent.Host.Features.Auth;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// The tools of one run, in one place, so the chat and the evaluation set (agent/eval) give the model exactly the same
/// tools: the role's bank reads (those with customer-written text between untrusted markers), every change behind
/// <see cref="ConfirmFirst"/>, the UI tools, and for the admin the isolated document reader.
/// </summary>
public static class AgentTools
{
    public static IList<AITool> For(BankToolSet bank, HostSession session, UiTools ui, IChatClient model)
    {
        IList<AITool> tools =
        [
            .. bank.Reads.Select(MarkUntrusted.Around),
            .. bank.Changes.Select(c => new ConfirmFirst(c, session, ui)),
            .. ui.Tools,
        ];
        if (bank.Find("customer_document") is { } documentFile)
        {
            tools.Add(new DocumentReader(model, documentFile).Tool);
        }

        return tools;
    }
}
