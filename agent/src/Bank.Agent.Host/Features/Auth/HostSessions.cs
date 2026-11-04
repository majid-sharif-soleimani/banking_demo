using System.Buffers.Text;
using System.Security.Cryptography;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.Options;

namespace Bank.Agent.Host.Features.Auth;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>How long an unused host session lives; the same as identity's Identity:IdleTimeout.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often <see cref="SessionSweeper"/> looks for idle sessions whose browser never came back.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    public GatewayOptions Gateway { get; set; } = new();

    public sealed class GatewayOptions
    {
        /// <summary>The gateway's shared key (X-Api-Key). A secret: user secrets locally, env/agent.env at home.</summary>
        public string? ApiKey { get; set; }
    }
}

/// <summary>
/// One logged-in browser: the bank token, who it acts for and its one open chat (phase 2 design plan, decision A9).
/// Lives only in this process (stage 1 plan, slice 2): the token is never written to disk or sent to the browser.
/// </summary>
public sealed class HostSession
{
    public required string Id { get; init; }

    public required string BankToken { get; init; }

    public required Principal Principal { get; init; }

    public required Guid ChatId { get; init; }

    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Set when a newer login of the same user ended this session; told to its browser once, then forgotten.</summary>
    public ChatCloseReason? EndedBy { get; set; }

    /// <summary>
    /// What waits for the person (phase 2 stage 3): a change to confirm, a form to fill in, a document to upload. Kept
    /// here, in memory, by id; the browser answers with that id, and each one can be answered once. They end with the
    /// session.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, PendingAction> Pending { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// One writer at a time on this session's chat: a run holds it from the person's message to the end of the answer,
    /// an action of the person holds it while its outcome is stored. Two tabs of one browser share the cookie, so they
    /// share this lock; a second run is refused with run_in_progress instead of interleaving two answers. The host is one
    /// process and sessions live in its memory, so an in-memory lock is enough.
    /// </summary>
    public SemaphoreSlim ChatLock { get; } = new(1, 1);
}

/// <summary>Something the agent asked the person for. The id is random and unguessable, like the cookie.</summary>
public abstract record PendingAction(string Id);

/// <summary>A change the model asked for: the tool and the exact arguments that will be sent on Confirm.</summary>
public sealed record PendingChange(string Id, string Tool, string Title, System.Text.Json.JsonElement Arguments) : PendingAction(Id);

/// <summary>A form shown to the person; its values are checked against these fields.</summary>
public sealed record OpenForm(string Id, string Title, IReadOnlyList<FormField> Fields) : PendingAction(Id);

/// <summary>A document the agent asked the person to upload for one of their loan applications.</summary>
public sealed record DocumentRequest(string Id, Guid ApplicationId, string Description) : PendingAction(Id);

public abstract record SessionLookup
{
    public sealed record Active(HostSession Session) : SessionLookup;

    /// <summary>Unused for the idle timeout; already removed. Its chat still has to be closed.</summary>
    public sealed record Idle(HostSession Session) : SessionLookup;

    /// <summary>Ended by a newer login (its chat is already closed); already removed after this one answer.</summary>
    public sealed record Ended(HostSession Session, ChatCloseReason Reason) : SessionLookup;

    public sealed record Unknown : SessionLookup;
}

/// <summary>The host sessions, in memory behind one lock. A restart logs everyone out, like identity.</summary>
public sealed class HostSessions(TimeProvider time, IOptions<AgentOptions> options)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, HostSession> byId = new(StringComparer.Ordinal);

    public HostSession Start(string bankToken, Principal principal, Guid chatId)
    {
        // 32 random bytes, URL-safe: the cookie value. It says nothing about the user or the token.
        var session = new HostSession
        {
            Id = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            BankToken = bankToken,
            Principal = principal,
            ChatId = chatId,
            LastSeen = time.GetUtcNow(),
        };
        lock (gate)
        {
            byId[session.Id] = session;
        }

        return session;
    }

    /// <summary>Finds the session of a cookie value and marks it used now, unless it has been idle too long.</summary>
    public SessionLookup Use(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return new SessionLookup.Unknown();
        }

        var now = time.GetUtcNow();
        lock (gate)
        {
            if (!byId.TryGetValue(id, out var session))
            {
                return new SessionLookup.Unknown();
            }

            if (session.EndedBy is { } reason)
            {
                byId.Remove(id);
                return new SessionLookup.Ended(session, reason);
            }

            if (SessionEnd.IsIdle(session.LastSeen, now, options.Value.IdleTimeout))
            {
                byId.Remove(id);
                return new SessionLookup.Idle(session);
            }

            session.LastSeen = now;
            return new SessionLookup.Active(session);
        }
    }

    public HostSession? End(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (gate)
        {
            return byId.Remove(id, out var session) ? session : null;
        }
    }

    /// <summary>
    /// The idle sessions that still own an open chat (an ended session's chat was closed by the login that ended it).
    /// Nothing is removed here: the sweeper closes their chats first and then calls <see cref="End"/>, so a failed close
    /// is tried again on the next sweep. Ended sessions nobody asked about are removed on the way.
    /// </summary>
    public IReadOnlyList<HostSession> FindIdle()
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            var idle = IdleLocked(now);
            foreach (var ended in idle.Where(s => s.EndedBy is not null))
            {
                byId.Remove(ended.Id);
            }

            return [.. idle.Where(s => s.EndedBy is null)];
        }
    }

    /// <summary>
    /// A new login of <paramref name="username"/>: every other session of that user ends (like identity, the newer login
    /// wins) and stays only to tell its browser why, once. Idle sessions of anyone, including ended ones nobody asked
    /// about, are removed on the way, which keeps the store bounded. Returns the sessions whose chats must be closed.
    /// </summary>
    public IReadOnlyList<HostSession> EndForNewLogin(string username)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            var idle = IdleLocked(now);
            foreach (var session in idle)
            {
                byId.Remove(session.Id);
            }

            var replaced = byId.Values.Where(s => s.Principal.Username == username && s.EndedBy is null).ToList();
            foreach (var session in replaced)
            {
                session.EndedBy = ChatCloseReason.SessionReplaced;
            }

            return [.. idle.Where(s => s.EndedBy is null), .. replaced];
        }
    }

    /// <summary>Every session unused for the idle timeout, ended or not. Call it inside the lock.</summary>
    private List<HostSession> IdleLocked(DateTimeOffset now) =>
        [.. byId.Values.Where(s => SessionEnd.IsIdle(s.LastSeen, now, options.Value.IdleTimeout))];
}
