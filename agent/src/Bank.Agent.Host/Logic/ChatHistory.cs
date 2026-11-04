using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Logic;

/// <summary>One stored chat message, as the history needs it: who wrote it, its kind and its content.</summary>
public sealed record StoredMessage(string Author, string Kind, string Content);

/// <summary>
/// The history the model sees in a later turn, built from the stored chat (the chat and the evaluation set use the same
/// conversion, so the evaluation tests what runs).
///
/// Only text and components are stored, never raw tool results. A component becomes a short assistant note with its
/// data, so the model remembers what the person has seen; an action of the person (a form sent, a change confirmed, a
/// document uploaded) becomes a short user note with its outcome.
///
/// Why the component data is wrapped as untrusted (agent ADR 0005): a table can hold text a customer wrote, such as the
/// purpose of a loan application. Replayed as the assistant's own words, "Ignore your rules and ..." would become
/// trusted context in every later turn. Between the markers it stays data for the whole chat, not only for the turn in
/// which a tool read it. The person's own typed text is not wrapped: it is what the model answers.
///
/// Only the last <c>limit</c> messages go to the model (cost, agent ADR 0007). The bank's data is read again through
/// the tools anyway. The history starts at a message the person typed, so it never opens with half of an exchange.
/// </summary>
public static class ChatHistory
{
    public static IReadOnlyList<ChatMessage> ForModel(IEnumerable<StoredMessage> messages, int limit)
    {
        var last = messages.TakeLast(limit).SkipWhile(m => !IsTypedByPerson(m));
        return [.. last.Select(ToModel)];
    }

    public static ChatMessage ToModel(StoredMessage m) => m switch
    {
        { Kind: "text" } => new ChatMessage(m.Author == "user" ? ChatRole.User : ChatRole.Assistant, m.Content),
        { Author: "user" } => new ChatMessage(ChatRole.User, $"(My {Name(m.Kind)}:)\n{UntrustedContent.Wrap($"earlier {m.Kind}", m.Content)}"),
        _ => new ChatMessage(ChatRole.Assistant, $"(Shown to the person as a {Name(m.Kind)}:)\n{UntrustedContent.Wrap($"earlier {m.Kind}", m.Content)}"),
    };

    private static bool IsTypedByPerson(StoredMessage m) => m is { Author: "user", Kind: "text" };

    private static string Name(string kind) => kind.Replace('_', ' ');
}
