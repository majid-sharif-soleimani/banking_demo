namespace Bank.Identity.Api.Logic;

public static class Roles
{
    /// <summary>Bank staff: the gateway forwards the call with X-Performed-By = the username.</summary>
    public const string Admin = "admin";

    /// <summary>A bank customer: the gateway forwards the call with X-Customer-Id = the customer id.</summary>
    public const string Customer = "customer";
}

/// <summary>Who a session acts for. <paramref name="CustomerId"/> and <paramref name="Name"/> are set for customers only.</summary>
public sealed record Principal(string Username, string Role, Guid? CustomerId = null, string? Name = null);

/// <summary>
/// One login. <paramref name="LastSeen"/> moves forward on every valid use (sliding expiry); <paramref name="ReplacedAt"/>
/// is set when the same username logged in again, so the old token can say why it stopped working.
/// </summary>
public sealed record SessionEntry(string Token, Principal Principal, DateTimeOffset LastSeen, DateTimeOffset? ReplacedAt = null);

public enum SessionState
{
    Valid,
    Unknown,
    Expired,
    Replaced,
}

/// <summary>
/// The session rules (workspace ADR 0007): one active session per username, a new login replaces the old one, and a
/// session ends after the idle timeout without a request. Pure: the store passes the time and the timeout in.
/// </summary>
public static class SessionRules
{
    public static DateTimeOffset ExpiresAt(SessionEntry entry, TimeSpan idleTimeout) => entry.LastSeen + idleTimeout;

    /// <summary>A replaced session says so even when it would also have expired: that is the more useful answer.</summary>
    public static SessionState Check(SessionEntry? entry, DateTimeOffset now, TimeSpan idleTimeout) =>
        entry is null ? SessionState.Unknown
        : entry.ReplacedAt is not null ? SessionState.Replaced
        : now >= ExpiresAt(entry, idleTimeout) ? SessionState.Expired
        : SessionState.Valid;

    /// <summary>
    /// An ended session is kept one more idle period, so its first request after the end can still say why (expired or
    /// replaced). After that it is forgotten and its token is simply unknown.
    /// </summary>
    public static bool CanForget(SessionEntry entry, DateTimeOffset now, TimeSpan idleTimeout) =>
        now >= (entry.ReplacedAt ?? ExpiresAt(entry, idleTimeout)) + idleTimeout;
}

/// <summary>
/// Who may log in (workspace ADR 0007). The admin user is hard-coded on purpose, for testing only: anyone who reads this
/// file can log in as staff. A customer logs in with the national id as both username and password.
/// </summary>
public static class Credentials
{
    public const string AdminUsername = "admin";

    private const string AdminPassword = "admin";

    /// <summary>The longest national id the Customers service stores.</summary>
    public const int MaxNationalIdLength = 32;

    public static bool IsAdmin(string? username, string? password) => username == AdminUsername && password == AdminPassword;

    /// <summary>The national id to look up when username and password are the same non-blank value; otherwise null.</summary>
    public static string? CustomerNationalId(string? username, string? password)
    {
        var id = username?.Trim();
        return !string.IsNullOrEmpty(id) && id.Length <= MaxNationalIdLength && id == password?.Trim() && id != AdminUsername ? id : null;
    }
}
