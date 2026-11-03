using Anthropic;
using Bank.Agent.Host.Features.AgUi;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

public sealed class ModelOptions
{
    public const string Section = "Agent:Model";

    /// <summary>The Claude model id (design plan ADR 24: the model comes from configuration).</summary>
    public string Name { get; set; } = "claude-opus-5-5";

    /// <summary>The Anthropic API key. A secret: user secrets locally, env/agent.env at home. Empty means the echo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The longest answer of one model call, in tokens.</summary>
    public int MaxOutputTokens { get; set; } = 8000;
}

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
        builder.Services.AddSingleton(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // Development and the tests run without a key: the echo of stage 1 answers instead of a model.
            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Agent:Model:ApiKey is required in Production. Set Agent__Model__ApiKey (env/agent.env at home, user secrets locally).");
            }

            builder.Services.AddSingleton<IChatClient, EchoChatClient>();
            return;
        }

        var anthropic = new AnthropicClient { ApiKey = options.ApiKey };
        builder.Services.AddSingleton(anthropic.AsIChatClient(options.Name));
    }
}
