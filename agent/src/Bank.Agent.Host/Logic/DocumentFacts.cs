using System.Text.Json;

namespace Bank.Agent.Host.Logic;

/// <summary>One fact taken from a document, such as ("Net salary", "32 400 SEK").</summary>
public sealed record DocumentFact(string Label, string Value);

/// <summary>
/// What the document reader may report about a customer's document: its kind, a short summary, a few facts, and any
/// text in it that looks like an instruction to an assistant (suspicious). Nothing else gets through.
/// </summary>
public sealed record DocumentFacts(string DocumentType, string Summary, IReadOnlyList<DocumentFact> Facts, IReadOnlyList<string> Suspicious)
{
    public const int MaxFacts = 20;

    public const int MaxTextLength = 300;

    public const int MaxSummaryLength = 1000;

    /// <summary>
    /// Checks the reader's answer: valid JSON of the fixed shape, within the limits. Anything else is refused, so a
    /// document that talks the reader into another answer gets no further than this check.
    /// </summary>
    public static DocumentFacts? Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        // Models sometimes wrap JSON in a code fence; take the outermost object.
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = json.RootElement;
            var type = Text(root, "documentType", MaxTextLength);
            var summary = Text(root, "summary", MaxSummaryLength);
            if (type is null || summary is null)
            {
                return null;
            }

            var facts = root.TryGetProperty("facts", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().ToList() : [];
            var suspicious = root.TryGetProperty("suspicious", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : [];
            if (facts.Count > MaxFacts || suspicious.Count > MaxFacts)
            {
                return null;
            }

            var factList = facts.Select(x => (Label: Text(x, "label", MaxTextLength), Value: Text(x, "value", MaxTextLength))).ToList();
            var suspiciousList = suspicious.Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToList();
            return factList.Any(x => x.Label is null || x.Value is null) || suspiciousList.Any(x => x is null || x.Length > MaxTextLength)
                ? null
                : new DocumentFacts(type, summary, [.. factList.Select(x => new DocumentFact(x.Label!, x.Value!))], [.. suspiciousList!]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name, int maxLength) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text && text.Length <= maxLength
            ? text
            : null;
}
