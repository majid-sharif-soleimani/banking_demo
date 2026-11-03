namespace Bank.Agent.Host.Logic;

/// <summary>Why a chat stopped taking messages. Stored in chat.closed_reason and sent to the chat UI as its code.</summary>
public enum ChatCloseReason
{
    Logout,
    SessionExpired,
    SessionReplaced,
    NewLogin,
}

/// <summary>When a session ends, and how that is named (phase 2 design plan, section 4.4).</summary>
public static class SessionEnd
{
    /// <summary>
    /// The close reason for a 401 code from identity, or null when the code does not mean the session ended. Identity
    /// explains an end once and then forgets the token (workspace ADR 0007), so a later <c>invalid_token</c> is an
    /// end whose reason was already lost: it counts as expired.
    /// </summary>
    public static ChatCloseReason? FromIdentityCode(string? code) => code switch
    {
        "session_expired" or "invalid_token" or "missing_token" => ChatCloseReason.SessionExpired,
        "session_replaced" => ChatCloseReason.SessionReplaced,
        _ => null,
    };

    public static string Code(ChatCloseReason reason) => reason switch
    {
        ChatCloseReason.Logout => "logout",
        ChatCloseReason.SessionExpired => "session_expired",
        ChatCloseReason.SessionReplaced => "session_replaced",
        ChatCloseReason.NewLogin => "new_login",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>
    /// A host session nobody used for <paramref name="idleTimeout"/> is gone: identity has ended the bank session by
    /// then, so the host forgets it too. Same edge as identity: idle at exactly the timeout.
    /// </summary>
    public static bool IsIdle(DateTimeOffset lastSeen, DateTimeOffset now, TimeSpan idleTimeout) => now - lastSeen >= idleTimeout;
}
