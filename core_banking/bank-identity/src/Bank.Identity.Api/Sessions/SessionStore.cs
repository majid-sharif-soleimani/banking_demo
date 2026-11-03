using System.Buffers.Text;
using System.Security.Cryptography;
using Bank.Identity.Api.Logic;
using Microsoft.Extensions.Options;

namespace Bank.Identity.Api.Sessions;

public sealed class IdentityOptions
{
    public const string Section = "Identity";

    /// <summary>A session ends after this long without a request (workspace ADR 0007: 5 minutes).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Where sessions live. In memory today (ADR 0007); a database or Redis later only replaces this class.</summary>
public interface ISessionStore
{
    /// <summary>A new session with a fresh token. An active session of the same username is marked replaced.</summary>
    SessionEntry Start(Principal principal);

    /// <summary>Checks a token; a valid session's idle clock restarts. An ended session is reported once, then forgotten.</summary>
    (SessionState State, SessionEntry? Session) Use(string token);

    /// <summary>Ends a valid session (logout). Returns the state the token was in before.</summary>
    SessionState End(string token);
}

/// <summary>
/// Sessions in a dictionary behind one lock: correct for a single instance, lost on restart (everyone logs in again).
/// The time is technical, not the business date, so it comes from <see cref="TimeProvider"/>, not IBusinessClock.
/// </summary>
public sealed class InMemorySessionStore(TimeProvider clock, IOptions<IdentityOptions> options) : ISessionStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, SessionEntry> byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activeTokenByUsername = new(StringComparer.Ordinal);

    private TimeSpan Idle => options.Value.IdleTimeout;

    public SessionEntry Start(Principal principal)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Forget(now);
            if (activeTokenByUsername.TryGetValue(principal.Username, out var old) && byToken.TryGetValue(old, out var previous))
            {
                byToken[old] = previous with { ReplacedAt = now };
            }

            // 32 random bytes: 256 bits, URL-safe text. Never derived from the user, never reused.
            var entry = new SessionEntry(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), principal, now);
            byToken[entry.Token] = entry;
            activeTokenByUsername[principal.Username] = entry.Token;
            return entry;
        }
    }

    public (SessionState State, SessionEntry? Session) Use(string token)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            var entry = byToken.GetValueOrDefault(token);
            var state = SessionRules.Check(entry, now, Idle);
            switch (state)
            {
                case SessionState.Valid:
                    entry = entry! with { LastSeen = now };
                    byToken[token] = entry;
                    break;
                case SessionState.Expired or SessionState.Replaced:
                    Remove(entry!);
                    break;
            }

            return (state, entry);
        }
    }

    public SessionState End(string token)
    {
        var (state, entry) = Use(token);
        if (state == SessionState.Valid)
        {
            lock (gate)
            {
                Remove(entry!);
            }
        }

        return state;
    }

    private void Remove(SessionEntry entry)
    {
        byToken.Remove(entry.Token);
        if (activeTokenByUsername.GetValueOrDefault(entry.Principal.Username) == entry.Token)
        {
            activeTokenByUsername.Remove(entry.Principal.Username);
        }
    }

    // On every login, so the dictionaries never grow beyond the users of the last few idle periods.
    private void Forget(DateTimeOffset now)
    {
        foreach (var entry in byToken.Values.Where(e => SessionRules.CanForget(e, now, Idle)).ToList())
        {
            Remove(entry);
        }
    }
}
