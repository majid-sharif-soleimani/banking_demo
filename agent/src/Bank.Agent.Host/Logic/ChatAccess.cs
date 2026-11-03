namespace Bank.Agent.Host.Logic;

public enum Role
{
    Customer,
    Admin,
}

/// <summary>Who a session acts for, as identity reported it at login (workspace ADR 0007). Never holds the bank token.</summary>
/// <param name="CustomerId">The customer's id; null for the admin.</param>
public sealed record Principal(string Username, Role Role, Guid? CustomerId, string Name);

/// <summary>The two facts about a chat that decide access: whose it is, and whether it still takes messages.</summary>
public sealed record ChatFacts(string Username, bool IsOpen);

/// <summary>
/// Who may read and write which chat (phase 2 design plan, section 7.3 question 6): a customer reads only their own
/// chats, the admin reads every chat, and only the owner writes, and only while the chat is open. Old chats are
/// read-only for everyone.
/// </summary>
public static class ChatAccess
{
    public static bool CanRead(Principal principal, ChatFacts chat) =>
        principal.Role == Role.Admin || IsOwner(principal, chat);

    public static bool CanWrite(Principal principal, ChatFacts chat) => chat.IsOpen && IsOwner(principal, chat);

    // Usernames are national ids or "admin", compared exactly, as identity does.
    private static bool IsOwner(Principal principal, ChatFacts chat) => string.Equals(principal.Username, chat.Username, StringComparison.Ordinal);
}
