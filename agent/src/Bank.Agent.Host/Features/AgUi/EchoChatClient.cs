using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>
/// Stands in for the language model in stage 1: answers "You said: ..." to the last user message, streamed word by word
/// like a model would. Stage 2 replaces it with the Claude client behind the same <see cref="IChatClient"/>.
/// </summary>
public sealed class EchoChatClient : IChatClient
{
    public const string Prefix = "You said: ";

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = string.Concat(await GetStreamingResponseAsync(messages, options, cancellationToken)
            .Select(update => update.Text).ToListAsync(cancellationToken));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        yield return new ChatResponseUpdate(ChatRole.Assistant, Prefix);
        var words = last.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, i < words.Length - 1 ? words[i] + " " : words[i]);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
