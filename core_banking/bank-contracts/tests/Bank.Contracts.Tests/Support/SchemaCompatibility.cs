using System.Text.Json.Nodes;
using Bank.Contracts.Schema;

namespace Bank.Contracts.Tests.Support;

/// <summary>
/// Checks that a schema version is a legal step from the previous one (ADR 0003). Only two steps exist:
/// ADDITION: same required fields, no field removed or changed, new fields optional. Every old reader still works.
/// MODEL: any other change; consumers that do not know the new MODEL send the message to the dead-letter topic.
/// REVISION is not used, so a REVISION bump is reported as a problem.
/// </summary>
public static class SchemaCompatibility
{
    public static IReadOnlyList<string> Check(SchemaKey older, JsonObject olderSchema, SchemaKey newer, JsonObject newerSchema)
    {
        var step = StepBetween(older, newer);
        if (step is null)
        {
            return [$"{older} -> {newer}: not a valid SchemaVer step; bump exactly one part by one and reset the parts after it."];
        }

        if (step == "MODEL")
        {
            return [];
        }

        if (step == "REVISION")
        {
            return [$"{older} -> {newer}: REVISION is not used; add only optional fields (ADDITION) or bump the MODEL."];
        }

        var problems = new List<string>();
        var oldProperties = olderSchema["properties"]?.AsObject() ?? [];
        var newProperties = newerSchema["properties"]?.AsObject() ?? [];

        foreach (var (name, oldProperty) in oldProperties)
        {
            if (newProperties[name] is not JsonObject newProperty)
            {
                problems.Add($"{newer}: '{name}' was removed; that needs a MODEL bump.");
            }
            else if (!JsonNode.DeepEquals(Shape(oldProperty!.AsObject()), Shape(newProperty)))
            {
                problems.Add($"{newer}: '{name}' changed type or format; that needs a MODEL bump.");
            }
        }

        var oldRequired = RequiredOf(olderSchema);
        var newRequired = RequiredOf(newerSchema);
        foreach (var name in oldRequired.Except(newRequired).Concat(newRequired.Except(oldRequired)))
        {
            problems.Add($"{newer}: '{name}' changed between required and optional; an ADDITION may only add optional fields.");
        }

        return problems;
    }

    private static string? StepBetween(SchemaKey older, SchemaKey newer) =>
        (newer.Model - older.Model, newer.Revision - older.Revision, newer.Addition - older.Addition) switch
        {
            (1, _, _) when newer is { Revision: 0, Addition: 0 } => "MODEL",
            (0, 1, _) when newer.Addition == 0 => "REVISION",
            (0, 0, 1) => "ADDITION",
            _ => null,
        };

    private static JsonObject Shape(JsonObject property) => new()
    {
        ["type"] = property["type"]?.DeepClone(),
        ["format"] = property["format"]?.DeepClone(),
    };

    private static HashSet<string> RequiredOf(JsonObject schema) =>
        (schema["required"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()).ToHashSet();
}
