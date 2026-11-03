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

    public Task<Chat?> FindAsync(Guid chatId, CancellationToken ct) =>
        db.Chats.Include(c => c.Messages.OrderBy(m => m.Sequence)).SingleOrDefaultAsync(c => c.Id == chatId, ct);

    /// <summary>Adds a message at the end. Only one run per chat at a time comes from one browser; the unique (chat, sequence) index catches a second.</summary>
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
        var entry = db.Messages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Forget the failed row, so the caller can try again on the same context.
            entry.State = EntityState.Detached;
            throw;
        }

        return message;
    }

    /// <summary>
    /// Adds a message after the person's action, trying again when a run appended at the same moment. The action has
    /// already happened at the bank, so its record must not be lost to a race for the next sequence number.
    /// </summary>
    public async Task AppendActionAsync(Chat chat, string kind, string content, string performedBy, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await AppendAsync(chat, "user", kind, content, performedBy, ct);
                return;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                await Task.Delay(100 * attempt, ct);
            }
        }
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
