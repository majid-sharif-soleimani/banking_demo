using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Bank.Agent.IntegrationTests.Support;

/// <summary>
/// Gives a scripted tool's answer the type a real MCP tool gives on success: <see cref="TextContent"/>, not a string.
/// Bank.Agent.Mcp.Tests pins that type against the SDK.
/// </summary>
public sealed class AnswersLikeMcp(AIFunction inner) : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var answer = await base.InvokeCoreAsync(arguments, cancellationToken);
        return new TextContent(answer is JsonElement json ? json.GetString() : (string?)answer);
    }
}
