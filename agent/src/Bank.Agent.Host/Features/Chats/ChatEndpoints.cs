using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Chats;

/// <summary>
/// The old chats (phase 2 design plan, requirement 2.1.6 and business question 6): GET /chats lists the chats a person
/// may read, newest first, and GET /chats/{id} returns one chat's messages. A customer reads only their own chats, the
/// admin every chat; both are read-only here. Every read of another person's chat is logged with the reader.
/// </summary>
public static class ChatEndpoints
{
    public const int ListLimit = 50;

    public sealed record MessageView(string Author, string Kind, string Content, DateTimeOffset CreatedAt);

    public sealed record ChatView(Guid Id, string Username, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, IReadOnlyList<MessageView> Messages);

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chats").WithTags("Chats");
        group.MapGet("/", ListAsync).WithSummary("The chats this person may read, newest first.");
        group.MapGet("/{id:guid}", GetAsync).WithSummary("One chat with its messages, read-only.");
    }

    private static async Task<IResult> ListAsync(HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        if (await guard.CheckAsync(http, ct) is not GuardResult.Allowed(var session))
        {
            return Problems.MissingSession();
        }

        return TypedResults.Ok(await chats.ListAsync(session.Principal, ListLimit, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, SessionGuard guard, ChatStore chats, ILogger<ChatView> log, CancellationToken ct)
    {
        if (await guard.CheckAsync(http, ct) is not GuardResult.Allowed(var session))
        {
            return Problems.MissingSession();
        }

        var chat = await chats.FindAsync(id, ct);
        if (chat is null || !ChatAccess.CanRead(session.Principal, new ChatFacts(chat.Username, chat.Status == ChatStatus.Open)))
        {
            return Problems.Of(StatusCodes.Status404NotFound, "chat_not_found", "No such chat.");
        }

        if (chat.Username != session.Principal.Username)
        {
            // Design plan section 9, "What to watch": the admin's reads of other people's chats are traceable.
            log.LogInformation("Chat {ChatId} read by {Reader}", chat.Id, session.Principal.Username);
        }

        return TypedResults.Ok(new ChatView(chat.Id, chat.Username, chat.Status.ToString(), chat.OpenedAt, chat.ClosedAt,
            [.. chat.Messages.OrderBy(m => m.Sequence).Select(m => new MessageView(m.Author, m.Kind, m.Content, m.CreatedAt))]));
    }
}
