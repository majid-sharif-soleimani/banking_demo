using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;
using Microsoft.EntityFrameworkCore;
using Bank.Agent.Host.Features.Agent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>What a run needs besides the session: the model, its settings, the bank tools and a logger.</summary>
public sealed record AgentParts(IChatClient Model, ModelOptions ModelOptions, IBankTools BankTools, ILogger<AgentParts> Log);

/// <summary>
/// POST /agent: one AG-UI run (phase 2 design plan, flow 4.2), written on the AG-UI .NET SDK's protocol types. The chat
/// lives on the server: of the messages the client sends, only the last user message is used, and the history the
/// model sees comes from schema "agent". The client cannot add or rewrite earlier turns, nor pick another chat: the
/// session's open chat is the only one it can write.
/// </summary>
public static class AgUiEndpoint
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = AGUIJsonUtilities.DefaultTypeInfoResolver };

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/agent", RunAsync)
            .WithTags("Agent")
            .WithSummary("Run the agent on the session's open chat (AG-UI over server-sent events).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    private static async Task<IResult> RunAsync(
        HttpContext http, SessionGuard guard, ChatStore chats, [AsParameters] AgentParts parts, CancellationToken ct)
    {
        RunAgentInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<RunAgentInput>(http.Request.Body, Json, ct);
        }
        catch (JsonException)
        {
            input = null;
        }

        var text = input?.Messages?.AsChatMessages().LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Trim();
        if (input is null || string.IsNullOrEmpty(text))
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_run", "Send an AG-UI run whose last user message has text.");
        }

        switch (await guard.CheckAsync(http, ct))
        {
            // No session at all: plain HTTP 401 before any stream; the UI shows the login form.
            case GuardResult.Denied(var problem, "missing_session"):
                return problem;
            // A session that just ended, or a bank that does not answer: a failed run, so the UI gets the reason.
            case GuardResult.Denied(_, var code):
                return TypedResults.ServerSentEvents(Failed(input, code));
            case GuardResult.Allowed(var session):
                return TypedResults.ServerSentEvents(Events(input, text, session, chats, parts, ct));
            default:
                throw new InvalidOperationException("Unknown guard result.");
        }
    }

    private static async IAsyncEnumerable<SseItem<string>> Failed(RunAgentInput input, string code)
    {
        await Task.CompletedTask;
        yield return Item(new RunStartedEvent { ThreadId = input.ThreadId ?? "", RunId = RunId(input) });
        var title = code == "bank_unavailable"
            ? "The bank could not be reached. Try again in a moment."
            : Problems.Title(SessionEnd.FromIdentityCode(code) ?? ChatCloseReason.SessionExpired);
        yield return Item(new RunErrorEvent { Code = code, Message = title });
    }

    private static async IAsyncEnumerable<SseItem<string>> Events(
        RunAgentInput input, string text, HostSession session, ChatStore chats, AgentParts parts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var threadId = session.ChatId.ToString();
        var runId = RunId(input);
        yield return Item(new RunStartedEvent { ThreadId = threadId, RunId = runId });

        var chat = await chats.FindAsync(session.ChatId, ct);
        if (chat is null || !ChatAccess.CanWrite(session.Principal, new ChatFacts(chat.Username, chat.Status == ChatStatus.Open)))
        {
            yield return Item(new RunErrorEvent { Code = "chat_closed", Message = "This chat no longer takes messages." });
            yield break;
        }

        // One browser has one session and one chat; two tabs of it running at once would race for the next sequence
        // number. The unique (chat, sequence) index lets one through; the other gets a clear error instead of a broken stream.
        var appended = true;
        try
        {
            await chats.AppendAsync(chat, "user", MessageKind.Text, text, session.Principal.Username, ct);
        }
        catch (DbUpdateException)
        {
            appended = false;
        }

        if (!appended)
        {
            yield return Item(new RunErrorEvent { Code = "run_in_progress", Message = "Another message is still being answered. Try again." });
            yield break;
        }

        // The tools of this run: the role's bank tools (customers only; least privilege, design plan section 6) and the
        // UI tools. A customer's bank tools need the MCP server; if it does not answer, the run fails like a bank outage.
        var bank = await OpenBankToolsAsync(session, parts, ct);
        if (bank is null)
        {
            yield return Item(new RunErrorEvent { Code = "bank_unavailable", Message = "The bank could not be reached. Try again in a moment." });
            yield break;
        }

        await using (bank)
        {
            var ui = new UiTools();
            var agent = new ChatClientAgent(parts.Model, new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions.For(session.Principal),
                    Tools = [.. bank.Tools, .. ui.Tools],
                    MaxOutputTokens = parts.ModelOptions.MaxOutputTokens,
                },
            });
            var history = chat.Messages.OrderBy(m => m.Sequence).Select(ToModel).ToList();

            // The agent loop runs inside RunStreamingAsync: the model answers with text or with tool calls; the agent
            // runs the tools, gives the model their results and asks again, until the model answers with text only.
            // Here we only watch what comes out: text goes to the browser as it arrives, a shown table or card goes to
            // the browser as a tool call, and every tool call is logged.
            var turn = new TurnWriter(chat, chats, session.Principal.Username);
            var toolNames = new Dictionary<string, string>();
            var failed = false;
            await using var updates = agent.RunStreamingAsync(history, cancellationToken: ct).GetAsyncEnumerator(ct);
            while (true)
            {
                AgentResponseUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }

                    update = updates.Current;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Only the type: a model provider's message could echo the request.
                    parts.Log.LogWarning("The model failed in chat {ChatId}: {Error}", session.ChatId, ex.GetType().Name);
                    failed = true;
                    break;
                }

                LogToolCalls(update, toolNames, session, parts.Log);
                if (!string.IsNullOrEmpty(update.Text))
                {
                    foreach (var e in turn.Text(update.Text))
                    {
                        yield return Item(e);
                    }
                }

                foreach (var component in ui.TakeShown())
                {
                    foreach (var e in await turn.ComponentAsync(component, ct))
                    {
                        yield return Item(e);
                    }
                }
            }

            foreach (var e in await turn.EndTextAsync(ct))
            {
                yield return Item(e);
            }

            yield return failed
                ? Item(new RunErrorEvent { Code = "model_unavailable", Message = "The assistant could not answer. Try again in a moment." })
                : Item(new RunFinishedEvent { ThreadId = threadId, RunId = runId });
        }
    }

    private static async Task<BankToolSet?> OpenBankToolsAsync(HostSession session, AgentParts parts, CancellationToken ct)
    {
        if (session.Principal.Role != Role.Customer)
        {
            return BankToolSet.None;
        }

        try
        {
            return await parts.BankTools.OpenForCustomerAsync(session, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            parts.Log.LogWarning("The bank MCP server could not be reached: {Error}", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Every tool call, with chat id, role, tool name and outcome (design plan section 6: every action is traceable).
    /// Never the arguments or the result: they hold personal data.
    /// </summary>
    private static void LogToolCalls(AgentResponseUpdate update, Dictionary<string, string> toolNames, HostSession session, ILogger log)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    toolNames[call.CallId] = call.Name;
                    break;
                case FunctionResultContent result:
                    log.LogInformation("Tool {Tool} in chat {ChatId} for role {Role}: {Outcome}",
                        toolNames.GetValueOrDefault(result.CallId, "unknown"), session.ChatId, session.Principal.Role,
                        result.Exception is null ? "ok" : "failed");
                    break;
            }
        }
    }

    /// <summary>
    /// A stored message as the model sees it in a later turn. A table or card becomes a short assistant note with its
    /// data, so the model remembers what the person has already seen.
    /// </summary>
    private static AiChatMessage ToModel(Data.ChatMessage m) => m switch
    {
        { Author: "user" } => new AiChatMessage(ChatRole.User, m.Content),
        { Kind: MessageKind.Text } => new AiChatMessage(ChatRole.Assistant, m.Content),
        _ => new AiChatMessage(ChatRole.Assistant, $"(Shown to the person as a {m.Kind}: {m.Content})"),
    };

    private static string RunId(RunAgentInput input) => string.IsNullOrWhiteSpace(input.RunId) ? Guid.NewGuid().ToString() : input.RunId;

    internal static SseItem<string> Item(BaseEvent e) => new(JsonSerializer.Serialize(e, Json));
}
