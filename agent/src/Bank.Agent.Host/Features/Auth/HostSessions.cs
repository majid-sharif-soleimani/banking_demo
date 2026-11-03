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
}

public abstract record SessionLookup
{
    public sealed record Active(HostSession Session) : SessionLookup;

    /// <summary>Unused for the idle timeout; already removed. Its chat still has to be closed.</summary>
    public sealed record Idle(HostSession Session) : SessionLookup;

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

    /// <summary>Every session of a user (a new login replaces them all, like identity), plus every idle one.</summary>
    public IReadOnlyList<HostSession> EndForNewLogin(string username)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            var ended = byId.Values
                .Where(s => s.Principal.Username == username || SessionEnd.IsIdle(s.LastSeen, now, options.Value.IdleTimeout))
                .ToList();
            foreach (var session in ended)
            {
                byId.Remove(session.Id);
            }

            return ended;
        }
    }
}
