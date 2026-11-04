using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Bank.Agent.Host.Features.AgUi;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// The model and its cost limits (agent ADR 0007). Every value comes from configuration (design plan ADR 24); the
/// defaults are the cheap profile Majid chose on 2026-11-03.
/// </summary>
public sealed class ModelOptions
{
    public const string Section = "Agent:Model";

    /// <summary>The Claude model id of the agent.</summary>
    public string Name { get; set; } = "claude-haiku-5-5";

    /// <summary>The Claude model id of the document reader (<see cref="DocumentReader"/>). Its own setting, so the two can differ.</summary>
    public string ReaderName { get; set; } = "claude-haiku-5-5";

    /// <summary>The Anthropic API key. A secret: user secrets locally, env/agent.env at home. Empty means the echo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The longest answer of one model call, in tokens.</summary>
    public int MaxOutputTokens { get; set; } = 2000;

    /// <summary>How many stored messages, the newest, the model sees each turn (<c>ChatHistory.ForModel</c>).</summary>
    public int HistoryMessages { get; set; } = 20;

    /// <summary>How many runs one user may start per minute (<see cref="RunLimiter"/>).</summary>
    public int RunsPerMinute { get; set; } = 5;

    /// <summary>
    /// The input tokens one run may use over all its model calls (agent ADR 0009). Above it the run stops and says so.
    /// A guard against a run that goes on and on, not a budget to plan with: a normal answer stays far below it.
    /// </summary>
    public long MaxInputTokensPerRun { get; set; } = 150_000;

    /// <summary>Above this many input tokens in one model call, the provider removes old tool results from what the model reads.</summary>
    public long ClearToolResultsAbove { get; set; } = 30_000;

    /// <summary>How many of the newest tool results stay when old ones are removed.</summary>
    public long KeepToolResults { get; set; } = 5;

    /// <summary>Remove only if it saves at least this many tokens: removing also throws away the cache after that point.</summary>
    public long ClearToolResultsAtLeast { get; set; } = 5_000;

    /// <summary>The longest stored table or card replayed to the model in later turns, in characters (<c>ChatHistory</c>).</summary>
    public int HistoryComponentLength { get; set; } = ChatHistory.DefaultComponentLength;
}

/// <summary>The model behind the document reader; its own type so it can be a different, smaller model than the agent's.</summary>
public sealed record ReaderModel(IChatClient Client);

/// <summary>
/// Chooses the language model behind <see cref="IChatClient"/>.
///
/// <see cref="IChatClient"/> (Microsoft.Extensions.AI) is one interface for every model provider: send messages and
/// tools, get text and tool calls back. Anthropic's official SDK implements it with AsIChatClient, so the rest of the
/// host never names the provider; another provider would change only this file (decision A1).
/// </summary>
public static class LanguageModel
{
    public static void AddLanguageModel(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(ModelOptions.Section).Get<ModelOptions>() ?? new ModelOptions();
        if (options.MaxOutputTokens <= 0 || options.HistoryMessages <= 0 || options.RunsPerMinute <= 0
            || options.MaxInputTokensPerRun <= 0 || options.ClearToolResultsAbove <= 0 || options.KeepToolResults < 0
            || options.ClearToolResultsAtLeast < 0 || options.HistoryComponentLength <= 0)
        {
            throw new InvalidOperationException(
                "Agent:Model limits must be positive: MaxOutputTokens, HistoryMessages, RunsPerMinute, MaxInputTokensPerRun, ClearToolResultsAbove and HistoryComponentLength; KeepToolResults and ClearToolResultsAtLeast cannot be negative.");
        }

        builder.Services.AddSingleton(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // Development and the tests run without a key: the echo of stage 1 answers instead of a model. Anywhere else
            // (Staging, Production) a missing key stops the start, the same rule as the gateway key.
            if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Agent:Model:ApiKey is required outside Development. Set Agent__Model__ApiKey (env/agent.env at home, user secrets locally).");
            }

            builder.Services.AddSingleton<IChatClient, EchoChatClient>();
            builder.Services.AddSingleton(new ReaderModel(new EchoChatClient()));
            return;
        }

        var anthropic = new AnthropicClient { ApiKey = options.ApiKey };
        builder.Services.AddSingleton(Claude(anthropic, options));
        builder.Services.AddSingleton(new ReaderModel(anthropic.AsIChatClient(options.ReaderName)));
    }

    /// <summary>The agent's model with its cost controls. The evaluation set uses it too, so it measures what runs.</summary>
    public static IChatClient Claude(IAnthropicClient client, ModelOptions options) =>
        new CostControlledModel(client.Beta.AsIChatClient(options.Name), options);

    /// <summary>The tokens a call wrote to the provider's cache; they cost more than plain input. Zero for another model.</summary>
    public static long CacheWritten(UsageDetails usage) =>
        usage.AdditionalCounts?.GetValueOrDefault("CacheCreationInputTokens") ?? 0;

    /// <summary>
    /// A failure of the model for the log. The provider's own message says what is wrong (a spent limit, a bad request), so
    /// it is kept, cut short. Its status and message hold no chat content; any other exception's message might, so that
    /// logs its type only.
    /// </summary>
    public static string Describe(Exception error)
    {
        if (error is not AnthropicApiException api)
        {
            return error.GetType().Name;
        }

        var message = "";
        try
        {
            message = JsonDocument.Parse(api.ResponseBody).RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not the provider's usual shape: the status alone still says a lot.
        }

        return $"{api.GetType().Name} {(int)api.StatusCode}: {(message.Length > 300 ? message[..300] : message)}".TrimEnd(' ', ':');
    }

    /// <summary>
    /// Adds the two cost controls to every call (agent ADR 0009). In the agent loop every call sends everything before it
    /// again, so the same tokens are paid for over and over.
    ///
    /// Cache: the provider remembers what it read up to the last block, and the next call pays a fraction for that part.
    /// Clearing old tool results (context editing): once a call gets long, the provider replaces the oldest tool results
    /// with a short note before the model reads them. Nothing changes on our side; the note tells the model to read again
    /// if it needs them. Both are set here so that the rest of the host never names the provider.
    /// </summary>
    private sealed class CostControlledModel(IChatClient inner, ModelOptions model) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(messages, WithControls(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(messages, WithControls(options), cancellationToken);

        private ChatOptions WithControls(ChatOptions? options)
        {
            var controlled = options?.Clone() ?? new ChatOptions();
            var maxTokens = controlled.MaxOutputTokens ?? model.MaxOutputTokens;
            // The instructions go in here so that they can carry a cache point of their own (agent ADR 0011). The other
            // one moves with the last block, and its prefix stops matching when the history window slides: without this,
            // the first call of each such run wrote the tools and the instructions again.
            var instructions = controlled.Instructions;
            controlled.Instructions = null;
            controlled.RawRepresentationFactory = _ => new MessageCreateParams
            {
                Model = model.Name,
                MaxTokens = maxTokens,
                Messages = [],
                System = string.IsNullOrEmpty(instructions)
                    ? null
                    : (MessageCreateParamsSystem)new List<BetaTextBlockParam> { new() { Text = instructions, CacheControl = new BetaCacheControlEphemeral() } },
                CacheControl = new BetaCacheControlEphemeral(),
                ContextManagement = new BetaContextManagementConfig
                {
                    Edits =
                    [
                        new BetaClearToolUses20250919Edit
                        {
                            Trigger = new BetaInputTokensTrigger(model.ClearToolResultsAbove),
                            Keep = new BetaToolUsesKeep(model.KeepToolResults),
                            ClearAtLeast = new BetaInputTokensClearAtLeast(model.ClearToolResultsAtLeast),
                        },
                    ],
                },
            };
            return controlled;
        }
    }
}
