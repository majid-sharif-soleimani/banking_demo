using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>What one bank tool answered: whether it worked, and the bank's text.</summary>
public sealed record ToolAnswer(bool Done, string? Text);

/// <summary>
/// Reads a bank tool's answer. An MCP tool error (isError) is a refusal by the bank; anything else worked. On success a
/// real MCP tool gives <see cref="TextContent"/>, and its text is the bank's answer; serializing it would wrap the answer.
/// </summary>
public static class ToolResult
{
    public static ToolAnswer Read(object? result)
    {
        if (result is JsonElement { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("content", out var content))
        {
            var text = string.Concat(content.EnumerateArray().Where(c => c.TryGetProperty("text", out _)).Select(c => c.GetProperty("text").GetString()));
            var failed = json.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True;
            return new ToolAnswer(!failed, text);
        }

        return new ToolAnswer(true, result switch
        {
            null => null,
            string s => s,
            TextContent t => t.Text,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement e => e.GetRawText(),
            _ => JsonSerializer.Serialize(result),
        });
    }
}
