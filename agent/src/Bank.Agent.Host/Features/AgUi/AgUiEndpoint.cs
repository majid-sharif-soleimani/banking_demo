using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;
using Bank.Agent.Host.Features.Agent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>What a run needs besides the session: the models, their settings and limits, the bank tools and a logger.</summary>
public sealed record AgentParts(
    IChatClient Model, ReaderModel Reader, ModelOptions ModelOptions, RunLimiter Limiter, IBankTools BankTools, ILogger<AgentParts> Log);

/// <summary>
/// POST /agent: one AG-UI run (phase 2 design plan, flow 4.2), written on the AG-UI .NET SDK's protocol types. The chat
/// lives on the server: of the messages the client sends, only the last user message is used, and the history the
/// model sees comes from schema "agent". The client cannot add or rewrite earlier turns, nor pick another chat: the
/// session's open chat is the only one it can write.
///
/// A run, in five steps: check the session and the run limit, take the session's chat lock, store the person's
/// message, open the role's bank tools, and stream the agent's answer (text and components) while storing it.
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
                var title = code == "bank_unavailable"
                    ? BankUnavailable
                    : Problems.Title(SessionEnd.FromIdentityCode(code) ?? ChatCloseReason.SessionExpired);
                return TypedResults.ServerSentEvents(Failed(input, code, title));
            case GuardResult.Allowed(var session) when !parts.Limiter.TryStart(session.Principal.Username):
                parts.Log.LogWarning("Run limit reached in chat {ChatId} for role {Role}", session.ChatId, session.Principal.Role);
                return TypedResults.ServerSentEvents(Failed(input, "too_many_runs", "You sent many messages in a short time. Wait a minute and try again."));
            case GuardResult.Allowed(var session):
                return TypedResults.ServerSentEvents(Events(input, text, session, chats, parts, ct));
            default:
                throw new InvalidOperationException("Unknown guard result.");
        }
    }

    private const string BankUnavailable = "The bank could not be reached. Try again in a moment.";

    private static async IAsyncEnumerable<SseItem<string>> Failed(RunAgentInput input, string code, string message)
    {
        await Task.CompletedTask;
        yield return Item(new RunStartedEvent { ThreadId = input.ThreadId ?? "", RunId = RunId(input) });
        yield return Item(new RunErrorEvent { Code = code, Message = message });
    }

    private static async IAsyncEnumerable<SseItem<string>> Events(
        RunAgentInput input, string text, HostSession session, ChatStore chats, AgentParts parts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var threadId = session.ChatId.ToString();
        var runId = RunId(input);
        yield return Item(new RunStartedEvent { ThreadId = threadId, RunId = runId });

        // One writer per session at a time (HostSession.ChatLock). Two tabs of one browser share the session: the second
        // run gets a clear error instead of interleaving its answer with the first one.
        if (!await session.ChatLock.WaitAsync(0, ct))
        {
            yield return Item(new RunErrorEvent { Code = "run_in_progress", Message = "Another message is still being answered. Try again." });
            yield break;
        }

        var failed = false;
        try
        {
            await foreach (var e in TurnAsync(text, session, chats, parts, ct))
            {
                failed |= e is RunErrorEvent;
                yield return Item(e);
            }
        }
        finally
        {
            // Also runs when the browser goes away: ASP.NET Core disposes the stream, and the lock is free again.
            session.ChatLock.Release();
        }

        if (!failed)
        {
            yield return Item(new RunFinishedEvent { ThreadId = threadId, RunId = runId });
        }
    }

    /// <summary>
    /// One turn, under the chat lock: stores the person's message, then streams the agent's answer and stores it. Ends
    /// with a <see cref="RunErrorEvent"/> when something failed; the caller adds the finish otherwise.
    /// </summary>
    private static async IAsyncEnumerable<BaseEvent> TurnAsync(
        string text, HostSession session, ChatStore chats, AgentParts parts, [EnumeratorCancellation] CancellationToken ct)
    {
        var chat = await chats.FindAsync(session.ChatId, ct);
        if (chat is null || !ChatAccess.CanWrite(session.Principal, new ChatFacts(chat.Username, chat.Status == ChatStatus.Open)))
        {
            yield return new RunErrorEvent { Code = "chat_closed", Message = "This chat no longer takes messages." };
            yield break;
        }

        // The chat is tracked, so the new message also appears in chat.Messages, and the history below ends with it.
        await chats.AppendAsync(chat, "user", MessageKind.Text, text, session.Principal.Username, ct);

        // The bank tools need the MCP server; if it does not answer, the run fails like a bank outage.
        var bank = await OpenBankToolsAsync(session, parts, ct);
        if (bank is null)
        {
            yield return new RunErrorEvent { Code = "bank_unavailable", Message = BankUnavailable };
            yield break;
        }

        await using (bank)
        {
            var ui = new UiTools(session);
            var agent = BuildAgent(session, parts, bank, ui);
            var history = ChatHistory.ForModel(
                chat.Messages.OrderBy(m => m.Sequence).Select(m => new StoredMessage(m.Author, m.Kind, m.Content)), parts.ModelOptions.HistoryMessages, parts.ModelOptions.HistoryComponentLength);

            // The agent loop runs inside RunStreamingAsync: the model answers with text or with tool calls; the agent
            // runs the tools, gives the model their results and asks again, until the model answers with text only.
            // Here we only watch what comes out: text goes to the browser as it arrives, a shown table or card goes to
            // the browser as a tool call, every tool call is logged, and the tokens are counted.
            var turn = new TurnWriter(chat, chats, session.Principal.Username);
            var toolNames = new Dictionary<string, string>();
            var usage = new UsageDetails();
            var failed = false;
            var overBudget = false;
            // Whether the person got something since the last tool result, and why the model's last call ended. Together
            // they catch a run that would otherwise just stop: see the check after the loop.
            var answered = false;
            ChatFinishReason? finishReason = null;
            await foreach (var update in UntilFailure(agent.RunStreamingAsync(history, cancellationToken: ct), session, parts.Log, ct))
            {
                if (update is null)
                {
                    failed = true;
                    break;
                }

                LogToolCalls(update, toolNames, session, parts.Log);
                answered &= !update.Contents.Any(c => c is FunctionResultContent);
                finishReason = update.FinishReason ?? finishReason;
                foreach (var counted in update.Contents.OfType<UsageContent>())
                {
                    usage.Add(counted.Details);
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    answered = true;
                    foreach (var e in turn.Text(update.Text))
                    {
                        yield return e;
                    }
                }

                foreach (var component in ui.TakeShown())
                {
                    answered = true;
                    foreach (var e in await turn.ComponentAsync(component, ct))
                    {
                        yield return e;
                    }
                }

                // Leaving the loop ends the agent's enumeration, so no further tool runs and no further model call is made.
                if (usage.InputTokenCount > parts.ModelOptions.MaxInputTokensPerRun)
                {
                    overBudget = true;
                    break;
                }
            }

            foreach (var e in await turn.EndTextAsync(ct))
            {
                yield return e;
            }

            // What the run cost, so spending is visible per chat (agent ADR 0007). Counts only, never content.
            parts.Log.LogInformation("Run in chat {ChatId} for role {Role} used {InputTokens} input ({CachedTokens} from the cache) and {OutputTokens} output tokens",
                session.ChatId, session.Principal.Role, usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0, usage.OutputTokenCount ?? 0);

            if (failed)
            {
                yield return new RunErrorEvent { Code = "model_unavailable", Message = "The assistant is not available right now. Try again later." };
            }
            else if (overBudget)
            {
                parts.Log.LogWarning("Run in chat {ChatId} for role {Role} stopped at its token limit ({MaxInputTokens} input tokens)",
                    session.ChatId, session.Principal.Role, parts.ModelOptions.MaxInputTokensPerRun);
                yield return new RunErrorEvent { Code = "run_budget", Message = "This request needs too much work for one message. Ask for a smaller part." };
            }
            else if (!answered || finishReason == ChatFinishReason.Length)
            {
                // The model ended without a word after its last tool, or hit the output limit (Agent:Model:MaxOutputTokens),
                // for example on a table too long to write. No exception comes with this, so without the check the person
                // would see the chat just stop.
                parts.Log.LogWarning("Run in chat {ChatId} for role {Role} ended with no answer (finish reason {FinishReason})",
                    session.ChatId, session.Principal.Role, finishReason?.Value ?? "none");
                yield return new RunErrorEvent { Code = "no_answer", Message = "The answer stopped early. Ask for a smaller part, for example one page of a list." };
            }
        }
    }

    /// <summary>
    /// The agent of one run: the role's instructions, and the tools of <see cref="AgentTools"/> (reads, changes behind
    /// ConfirmFirst, the UI tools and the document reader), with the configured output limit.
    /// </summary>
    private static ChatClientAgent BuildAgent(HostSession session, AgentParts parts, BankToolSet bank, UiTools ui) =>
        new(parts.Model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Instructions = AgentInstructions.For(session.Principal),
                Tools = AgentTools.For(bank, session, ui, parts.Reader.Client),
                MaxOutputTokens = parts.ModelOptions.MaxOutputTokens,
            },
        });

    /// <summary>
    /// Passes the model's updates on, and turns a failure of the model into one null at the end, so the caller can use
    /// a plain await foreach. C# allows no yield inside a try that has a catch, so the catch only remembers the failure.
    /// </summary>
    private static async IAsyncEnumerable<AgentResponseUpdate?> UntilFailure(
        IAsyncEnumerable<AgentResponseUpdate> updates, HostSession session, ILogger log, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var e = updates.GetAsyncEnumerator(ct);
        while (true)
        {
            AgentResponseUpdate? next;
            var failed = false;
            try
            {
                next = await e.MoveNextAsync() ? e.Current : null;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("The model failed in chat {ChatId}: {Error}", session.ChatId, LanguageModel.Describe(ex));
                next = null;
                failed = true;
            }

            if (failed)
            {
                yield return null;
                yield break;
            }

            if (next is null)
            {
                yield break;
            }

            yield return next;
        }
    }

    private static async Task<BankToolSet?> OpenBankToolsAsync(HostSession session, AgentParts parts, CancellationToken ct)
    {
        try
        {
            return await parts.BankTools.OpenAsync(session, ct);
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

    private static string RunId(RunAgentInput input) => string.IsNullOrWhiteSpace(input.RunId) ? Guid.NewGuid().ToString() : input.RunId;

    internal static SseItem<string> Item(BaseEvent e) => new(JsonSerializer.Serialize(e, Json));
}
