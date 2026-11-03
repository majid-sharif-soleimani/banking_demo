using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Bank.Agent.Host.Features.AgUi;

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
        HttpContext http, SessionGuard guard, ChatStore chats, IChatClient model, CancellationToken ct)
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
                return TypedResults.ServerSentEvents(Events(input, text, session, chats, model, ct));
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
        RunAgentInput input, string text, HostSession session, ChatStore chats, IChatClient model,
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
            await chats.AppendAsync(chat, "user", text, session.Principal.Username, ct);
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

        var history = chat.Messages.OrderBy(m => m.Sequence)
            .Select(m => new AiChatMessage(m.Author == "user" ? ChatRole.User : ChatRole.Assistant, m.Content))
            .ToList();

        var messageId = Guid.NewGuid().ToString();
        yield return Item(new TextMessageStartEvent { MessageId = messageId, Role = AGUIRoles.Assistant });
        var answer = new System.Text.StringBuilder();
        await foreach (var update in model.GetStreamingResponseAsync(history, cancellationToken: ct))
        {
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            answer.Append(update.Text);
            yield return Item(new TextMessageContentEvent { MessageId = messageId, Delta = update.Text });
        }

        yield return Item(new TextMessageEndEvent { MessageId = messageId });
        await chats.AppendAsync(chat, "assistant", answer.ToString(), session.Principal.Username, ct);
        yield return Item(new RunFinishedEvent { ThreadId = threadId, RunId = runId });
    }

    private static string RunId(RunAgentInput input) => string.IsNullOrWhiteSpace(input.RunId) ? Guid.NewGuid().ToString() : input.RunId;

    private static SseItem<string> Item(BaseEvent e) => new(JsonSerializer.Serialize(e, Json));
}
