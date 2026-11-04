using Bank.Agent.Host.Data;
using Bank.Agent.Host.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Agent.Host.Features.Chats;

/// <summary>
/// Opens, closes and appends to chats in schema "agent" (phase 2 design plan, decisions A10 and A12). Time is wall-clock
/// time: a chat belongs to a login, not to a business day.
/// </summary>
public sealed class ChatStore(AgentDbContext db, TimeProvider time)
{
    public async Task<Chat> OpenAsync(Principal principal, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var chat = new Chat
        {
            Id = Guid.CreateVersion7(now),
            Username = principal.Username,
            Role = principal.Role == Role.Admin ? "admin" : "customer",
            CustomerId = principal.CustomerId,
            Status = ChatStatus.Open,
            OpenedAt = now,
            CreatedAt = now,
            PerformedBy = principal.Username,
        };
        db.Chats.Add(chat);
        await db.SaveChangesAsync(ct);
        return chat;
    }

    /// <summary>Closes one chat if it is still open. Closing twice keeps the first reason.</summary>
    public Task CloseAsync(Guid chatId, ChatCloseReason reason, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var code = SessionEnd.Code(reason);
        return db.Chats.Where(c => c.Id == chatId && c.Status == ChatStatus.Open).ExecuteUpdateAsync(set => set
            .SetProperty(c => c.Status, ChatStatus.Closed)
            .SetProperty(c => c.ClosedAt, now)
            .SetProperty(c => c.ClosedReason, code), ct);
    }

    /// <summary>A new login closes every chat the user still has open, wherever it was opened.</summary>
    public Task CloseOpenChatsOfAsync(string username, ChatCloseReason reason, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var code = SessionEnd.Code(reason);
        return db.Chats.Where(c => c.Username == username && c.Status == ChatStatus.Open).ExecuteUpdateAsync(set => set
            .SetProperty(c => c.Status, ChatStatus.Closed)
            .SetProperty(c => c.ClosedAt, now)
            .SetProperty(c => c.ClosedReason, code), ct);
    }

    /// <summary>
    /// At startup: no host session survived the restart, so no chat can still be open. Closes the leftovers with
    /// session_expired (phase 2 design plan, decision A12).
    /// </summary>
    public Task<int> CloseAllOpenAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var code = SessionEnd.Code(ChatCloseReason.SessionExpired);
        return db.Chats.Where(c => c.Status == ChatStatus.Open).ExecuteUpdateAsync(set => set
            .SetProperty(c => c.Status, ChatStatus.Closed)
            .SetProperty(c => c.ClosedAt, now)
            .SetProperty(c => c.ClosedReason, code), ct);
    }

    /// <summary>A chat with all its messages, for a run (the history) and for reading an old chat.</summary>
    public Task<Chat?> FindAsync(Guid chatId, CancellationToken ct) =>
        db.Chats.Include(c => c.Messages.OrderBy(m => m.Sequence)).SingleOrDefaultAsync(c => c.Id == chatId, ct);

    /// <summary>A chat without its messages: enough to check who owns it and whether it is open.</summary>
    public Task<Chat?> FindHeaderAsync(Guid chatId, CancellationToken ct) =>
        db.Chats.AsNoTracking().SingleOrDefaultAsync(c => c.Id == chatId, ct);

    /// <summary>
    /// Adds a message at the end. Callers hold the session's <c>ChatLock</c>, so one writer appends at a time; the unique
    /// (chat, sequence) index stays as the last guard.
    /// </summary>
    public async Task<ChatMessage> AppendAsync(Chat chat, string author, string kind, string content, string performedBy, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var next = await db.Messages.Where(m => m.ChatId == chat.Id).MaxAsync(m => (int?)m.Sequence, ct) ?? 0;
        var message = new ChatMessage
        {
            Id = Guid.CreateVersion7(now),
            ChatId = chat.Id,
            Sequence = next + 1,
            Author = author,
            Kind = kind,
            Content = content,
            CreatedAt = now,
            PerformedBy = performedBy,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        return message;
    }

    /// <summary>The chats a person may read, newest first: their own, or every chat for the admin.</summary>
    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Principal reader, int limit, CancellationToken ct)
    {
        var chats = db.Chats.AsNoTracking();
        if (reader.Role != Role.Admin)
        {
            chats = chats.Where(c => c.Username == reader.Username);
        }

        return await chats.OrderByDescending(c => c.OpenedAt).Take(limit)
            .Select(c => new ChatSummary(
                c.Id, c.Username, c.Status.ToString(), c.OpenedAt, c.ClosedAt,
                c.Messages.Where(m => m.Author == "user" && m.Kind == MessageKind.Text).OrderBy(m => m.Sequence).Select(m => m.Content).FirstOrDefault()))
            .ToListAsync(ct);
    }
}

/// <summary>One line of the list of old chats. The first question stands in for a title.</summary>
public sealed record ChatSummary(Guid Id, string Username, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, string? FirstQuestion);
