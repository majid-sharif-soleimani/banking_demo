using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Chats;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>
/// Turns one answer of the agent into AG-UI events and stored messages.
///
/// An answer can mix text and components: "Here are your accounts", a table, "Your savings account has the most". AG-UI
/// sends text as a text message (start, pieces, end) and a component as a tool call (start, arguments, end, result),
/// which the chat UI draws as a table or a card. This class keeps the order: when a component comes, the text so far is
/// closed and stored first. Each piece becomes its own row in chat_message, with its kind.
/// </summary>
public sealed class TurnWriter(Chat chat, ChatStore chats, string performedBy)
{
    private readonly StringBuilder text = new();
    private string? textMessageId;

    /// <summary>A piece of streamed text: starts a text message if none is open.</summary>
    public IEnumerable<BaseEvent> Text(string delta)
    {
        if (textMessageId is null)
        {
            textMessageId = Guid.NewGuid().ToString();
            yield return new TextMessageStartEvent { MessageId = textMessageId, Role = AGUIRoles.Assistant };
        }

        text.Append(delta);
        yield return new TextMessageContentEvent { MessageId = textMessageId, Delta = delta };
    }

    /// <summary>A shown table or card: closes the open text, then sends the component as a finished tool call.</summary>
    public async Task<IReadOnlyList<BaseEvent>> ComponentAsync(UiComponent component, CancellationToken ct)
    {
        var events = new List<BaseEvent>(await EndTextAsync(ct));
        var callId = Guid.NewGuid().ToString();
        events.Add(new ToolCallStartEvent { ToolCallId = callId, ToolCallName = component.Tool });
        events.Add(new ToolCallArgsEvent { ToolCallId = callId, Delta = component.Data.GetRawText() });
        events.Add(new ToolCallEndEvent { ToolCallId = callId });
        // A result marks the tool call as done in the UI; there is nothing for the browser to answer.
        events.Add(new ToolCallResultEvent { MessageId = Guid.NewGuid().ToString(), ToolCallId = callId, Content = "shown", Role = AGUIRoles.Tool });
        await chats.AppendAsync(chat, "assistant", component.Kind, component.Data.GetRawText(), performedBy, ct);
        return events;
    }

    /// <summary>Closes and stores the open text message, if any.</summary>
    public async Task<IReadOnlyList<BaseEvent>> EndTextAsync(CancellationToken ct)
    {
        if (textMessageId is null)
        {
            return [];
        }

        var end = new TextMessageEndEvent { MessageId = textMessageId };
        await chats.AppendAsync(chat, "assistant", MessageKind.Text, text.ToString(), performedBy, ct);
        textMessageId = null;
        text.Clear();
        return [end];
    }
}
