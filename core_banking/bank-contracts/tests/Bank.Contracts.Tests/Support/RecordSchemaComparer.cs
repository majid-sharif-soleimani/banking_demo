using System.Reflection;
using System.Text.Json.Nodes;

namespace Bank.Contracts.Tests.Support;

/// <summary>
/// Compares a C# record with its JSON Schema: same property names (camelCase), same JSON types and formats,
/// and "required" exactly for the non-nullable properties. Returns one readable line per problem.
/// </summary>
public static class RecordSchemaComparer
{
    private sealed record JsonShape(string Type, string? Format = null);

    private static readonly Dictionary<Type, JsonShape> Shapes = new()
    {
        [typeof(string)] = new("string"),
        [typeof(Guid)] = new("string", "uuid"),
        [typeof(DateOnly)] = new("string", "date"),
        [typeof(DateTimeOffset)] = new("string", "date-time"),
        [typeof(int)] = new("integer"),
        [typeof(long)] = new("integer"),
        [typeof(bool)] = new("boolean"),
        [typeof(Money)] = new("object"),
    };

    public static IReadOnlyList<string> Compare(Type recordType, JsonObject schema)
    {
        var problems = new List<string>();
        var schemaProperties = schema["properties"]?.AsObject() ?? [];
        var required = (schema["required"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()).ToHashSet();
        var nullability = new NullabilityInfoContext();

        var recordProperties = recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => JsonName(p.Name));

        foreach (var name in schemaProperties.Select(p => p.Key).Where(n => !recordProperties.ContainsKey(n)))
        {
            problems.Add($"'{name}': schema property is not in the record {recordType.Name}.");
        }

        foreach (var (name, property) in recordProperties)
        {
            if (schemaProperties[name] is not JsonObject propertySchema)
            {
                problems.Add($"'{name}': record property is not in the schema.");
                continue;
            }

            var isNullable = nullability.Create(property).ReadState == NullabilityState.Nullable;
            var clrType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (clrType == typeof(decimal) || clrType == typeof(double) || clrType == typeof(float))
            {
                problems.Add($"'{name}': bare {clrType.Name.ToLowerInvariant()} is not allowed in a contract; use Money (amount with currency).");
                continue;
            }

            if (!Shapes.TryGetValue(clrType, out var shape))
            {
                problems.Add($"'{name}': no JSON mapping for {clrType.Name}; add it to {nameof(RecordSchemaComparer)}.");
                continue;
            }

            problems.AddRange(CompareShape(name, propertySchema, shape, isNullable));

            if (clrType == typeof(Money))
            {
                problems.AddRange(CompareMoney(name, propertySchema));
            }

            if (isNullable && required.Contains(name))
            {
                problems.Add($"'{name}': nullable in the record, so it must not be required.");
            }

            if (!isNullable && !required.Contains(name))
            {
                problems.Add($"'{name}': non-nullable in the record, so it must be required.");
            }
        }

        return problems;
    }

    private static IEnumerable<string> CompareShape(string name, JsonObject propertySchema, JsonShape shape, bool isNullable)
    {
        var expectedTypes = isNullable ? new[] { shape.Type, "null" } : [shape.Type];
        var actualTypes = TypesOf(propertySchema);

        if (!actualTypes.Order().SequenceEqual(expectedTypes.Order()))
        {
            yield return $"'{name}': schema type is [{string.Join(", ", actualTypes)}], expected [{string.Join(", ", expectedTypes)}].";
        }

        var format = propertySchema["format"]?.GetValue<string>();
        if (format != shape.Format)
        {
            yield return $"'{name}': schema format is '{format}', expected '{shape.Format}'.";
        }
    }

    private static IEnumerable<string> CompareMoney(string name, JsonObject propertySchema)
    {
        var properties = propertySchema["properties"]?.AsObject();
        var required = (propertySchema["required"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()).ToHashSet();

        foreach (var part in new[] { "amount", "currency" })
        {
            if (properties?[part] is not JsonObject partSchema || !TypesOf(partSchema).SequenceEqual(["string"]) || !required.Contains(part))
            {
                yield return $"'{name}': Money needs a required string '{part}'.";
            }
        }
    }

    internal static string[] TypesOf(JsonObject propertySchema) => propertySchema["type"] switch
    {
        JsonArray types => types.Select(t => t!.GetValue<string>()).ToArray(),
        JsonValue type => [type.GetValue<string>()],
        _ => [],
    };

    private static string JsonName(string propertyName) =>
        ContractJson.Options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;
}
