using Anthropic;
using Bank.Agent.Host.Features.AgUi;
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
    public string Name { get; set; } = "claude-sonnet-5-5";

    /// <summary>The Claude model id of the document reader (<see cref="DocumentReader"/>): a smaller model is enough to list facts.</summary>
    public string ReaderName { get; set; } = "claude-haiku-5-5";

    /// <summary>The Anthropic API key. A secret: user secrets locally, env/agent.env at home. Empty means the echo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The longest answer of one model call, in tokens.</summary>
    public int MaxOutputTokens { get; set; } = 2000;

    /// <summary>How many stored messages, the newest, the model sees each turn (<c>ChatHistory.ForModel</c>).</summary>
    public int HistoryMessages { get; set; } = 20;

    /// <summary>How many runs one user may start per minute (<see cref="RunLimiter"/>).</summary>
    public int RunsPerMinute { get; set; } = 5;
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
        if (options.MaxOutputTokens <= 0 || options.HistoryMessages <= 0 || options.RunsPerMinute <= 0)
        {
            throw new InvalidOperationException("Agent:Model:MaxOutputTokens, HistoryMessages and RunsPerMinute must be positive.");
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
        builder.Services.AddSingleton(anthropic.AsIChatClient(options.Name));
        builder.Services.AddSingleton(new ReaderModel(anthropic.AsIChatClient(options.ReaderName)));
    }
}
