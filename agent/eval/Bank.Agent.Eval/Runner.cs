using System.Text;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Eval;

/// <summary>
/// Runs one case the way the chat would: the same instructions (<see cref="AgentInstructions"/>), the same tools
/// (<see cref="AgentTools"/>, over <see cref="FakeBank"/>), the same agent loop (ChatClientAgent). It records which
/// tools the model called and which components it showed, and keeps the last answer.
///
/// The history between the messages of a case is kept like the chat keeps it: only text and components are stored, in
/// the order <c>TurnWriter</c> stores them, and <see cref="ChatHistory.ForModel"/> builds what the model sees. So a
/// multi-turn case tests a model that, as in the chat, does not remember raw tool results.
/// </summary>
public sealed class Runner(IChatClient model, IChatClient reader, int maxOutputTokens, int historyMessages)
{
    public async Task<EvalRun> RunAsync(EvalCase c, CancellationToken ct)
    {
        var principal = c.Role == "admin"
            ? new Principal("admin", Role.Admin, null, "Admin")
            : new Principal("eval-customer", Role.Customer, FakeBank.Customer, "Sara Lind");
        var session = new HostSession { Id = "eval", BankToken = "not-used", Principal = principal, ChatId = Guid.NewGuid() };
        var ui = new UiTools(session);
        var called = new List<string>();
        var tools = AgentTools.For(FakeBank.For(c.Role), session, ui, reader)
            .Select(t => t is AIFunction f ? new Recorded(f, called) : t)
            .ToList();
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = AgentInstructions.For(principal), Tools = tools, MaxOutputTokens = maxOutputTokens },
        });

        var stored = new List<StoredMessage>();
        var shown = new List<string>();
        var answer = "";
        foreach (var message in c.Messages)
        {
            stored.Add(new StoredMessage("user", MessageKind.Text, message));
            var pending = new StringBuilder();
            var turnText = new StringBuilder();
            await foreach (var update in agent.RunStreamingAsync(ChatHistory.ForModel(stored, historyMessages), cancellationToken: ct))
            {
                pending.Append(update.Text);
                foreach (var component in ui.TakeShown())
                {
                    // As TurnWriter: the text so far is stored first, then the component.
                    Flush(pending, turnText, stored);
                    stored.Add(new StoredMessage("assistant", component.Kind, component.Data.GetRawText()));
                    shown.Add(component.Tool);
                }
            }

            Flush(pending, turnText, stored);
            answer = turnText.ToString();
        }

        return new EvalRun(called, shown, answer);
    }

    private static void Flush(StringBuilder pending, StringBuilder turnText, List<StoredMessage> stored)
    {
        if (pending.Length == 0)
        {
            return;
        }

        stored.Add(new StoredMessage("assistant", MessageKind.Text, pending.ToString()));
        turnText.Append(pending);
        pending.Clear();
    }

    /// <summary>Notes the name of every tool the model calls, then runs it as usual.</summary>
    private sealed class Recorded(AIFunction inner, List<string> called) : DelegatingAIFunction(inner)
    {
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            lock (called)
            {
                called.Add(Name);
            }

            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
