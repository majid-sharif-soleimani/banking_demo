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
/// </summary>
public sealed class Runner(IChatClient model, int maxOutputTokens)
{
    public async Task<EvalRun> RunAsync(EvalCase c, CancellationToken ct)
    {
        var principal = c.Role == "admin"
            ? new Principal("admin", Role.Admin, null, "Admin")
            : new Principal("eval-customer", Role.Customer, FakeBank.Customer, "Sara Lind");
        var session = new HostSession { Id = "eval", BankToken = "not-used", Principal = principal, ChatId = Guid.NewGuid() };
        var ui = new UiTools(session);
        var called = new List<string>();
        var tools = AgentTools.For(FakeBank.For(c.Role), session, ui, model)
            .Select(t => t is AIFunction f ? new Recorded(f, called) : t)
            .ToList();
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = AgentInstructions.For(principal), Tools = tools, MaxOutputTokens = maxOutputTokens },
        });

        var history = new List<ChatMessage>();
        var shown = new List<string>();
        var answer = "";
        foreach (var message in c.Messages)
        {
            history.Add(new ChatMessage(ChatRole.User, message));
            var response = await agent.RunAsync(history, cancellationToken: ct);
            history.AddRange(response.Messages);
            shown.AddRange(ui.TakeShown().Select(s => s.Tool));
            answer = response.Text;
        }

        return new EvalRun(called, shown, answer);
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
