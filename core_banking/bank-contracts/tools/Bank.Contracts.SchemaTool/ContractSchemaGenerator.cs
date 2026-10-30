using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Bank.Contracts.Schema;

namespace Bank.Contracts.SchemaTool;

/// <summary>
/// Generates the JSON Schema of an event record (ADR 0003). The record is the source of truth: the shape comes from
/// <see cref="JsonSchemaExporter"/> with <see cref="ContractJson.Options"/>, and descriptions and constraints come
/// from attributes on the record, or on <see cref="IIntegrationEvent"/> for the common fields. A property type that
/// implements <see cref="IJsonSchemaProvider"/> (such as <see cref="Money"/>) describes its own shape.
/// </summary>
public static class ContractSchemaGenerator
{
    private const string Draft = "https://json-schema.org/draft/2020-12/schema";

    private static readonly Type[] FloatingTypes = [typeof(decimal), typeof(double), typeof(float)];

    private static readonly MethodInfo SchemaOfMethod =
        typeof(ContractSchemaGenerator).GetMethod(nameof(SchemaOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static JsonObject Generate(Type recordType, SchemaKey key)
    {
        var description = recordType.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"{recordType.Name} needs a [Description] attribute; it becomes the schema description.");

        var exporterOptions = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        var generated = ContractJson.Options.GetJsonSchemaAsNode(recordType, exporterOptions).AsObject();

        // The exporter's TransformSchemaNode callback is skipped for some repeated types (a second Money field),
        // so own schemas and the attributes are applied here, property by property, from the record itself.
        var properties = generated["properties"]!.AsObject();
        var nullability = new NullabilityInfoContext();
        foreach (var property in recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var jsonName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var exported = properties[jsonName];
            var schema = PropertySchema(property, jsonName, exported, nullability);
            if (!ReferenceEquals(schema, exported))
            {
                properties[jsonName] = schema;
            }
        }

        var root = new JsonObject
        {
            ["$schema"] = Draft,
            ["self"] = new JsonObject
            {
                ["vendor"] = key.Vendor,
                ["name"] = key.Name,
                ["format"] = key.Format,
                ["version"] = key.Version,
            },
            ["description"] = description,
        };
        foreach (var (name, value) in generated)
        {
            root[name] = value?.DeepClone();
        }

        return root;
    }

    private static JsonObject PropertySchema(PropertyInfo property, string jsonName, JsonNode? exported, NullabilityInfoContext nullability)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (FloatingTypes.Contains(type))
        {
            throw new NotSupportedException(
                $"'{jsonName}' is a bare {type.Name}; amounts in contracts are Money, which carries its currency.");
        }

        var own = OwnSchema(type, jsonName);
        var schema = own is not null
            ? WithOwnSchema(own, exported, allowsNull: nullability.Create(property).ReadState == NullabilityState.Nullable)
            : exported as JsonObject ?? throw new NotSupportedException(
                $"'{jsonName}' of type {type.Name} has no JSON shape the generator understands; " +
                $"contracts hold flat fields and types that implement {nameof(IJsonSchemaProvider)}.");

        ApplyAttributes(schema, jsonName, property);
        return schema;
    }

    /// <summary>
    /// The schema a type gives of itself through <see cref="IJsonSchemaProvider"/>, or null when it does not implement
    /// it and the exporter's shape is used. A type with a custom converter must implement it: the exporter cannot see
    /// what the converter writes and would describe it as "anything".
    /// </summary>
    private static JsonObject? OwnSchema(Type type, string jsonName)
    {
        if (type.IsAssignableTo(typeof(IJsonSchemaProvider)))
        {
            return (JsonObject)SchemaOfMethod.MakeGenericMethod(type).Invoke(null, null)!;
        }

        if (type.GetCustomAttribute<JsonConverterAttribute>() is not null)
        {
            throw new NotSupportedException(
                $"'{jsonName}' of type {type.Name} has a custom JsonConverter, so its shape cannot be generated; " +
                $"implement {nameof(IJsonSchemaProvider)} on {type.Name}, describing what the converter writes.");
        }

        return null;
    }

    // A static abstract member can only be called through a type parameter; reflection supplies the type.
    private static JsonObject SchemaOf<T>() where T : IJsonSchemaProvider => T.JsonSchema();

    private static JsonObject WithOwnSchema(JsonObject own, JsonNode? exported, bool allowsNull)
    {
        if (allowsNull)
        {
            own["type"] = new JsonArray(own["type"]!.GetValue<string>(), "null");
        }

        if (exported is JsonObject original && original.TryGetPropertyValue("default", out var defaultValue))
        {
            own["default"] = defaultValue?.DeepClone();
        }

        return own;
    }

    private static void ApplyAttributes(JsonObject schema, string jsonName, ICustomAttributeProvider? provider)
    {
        var own = provider?.GetCustomAttributes(inherit: true).OfType<Attribute>() ?? [];
        var common = CommonFieldAttributes(jsonName);

        foreach (var attribute in common.Where(c => !own.Any(o => o.GetType() == c.GetType())).Concat(own))
        {
            switch (attribute)
            {
                case DescriptionAttribute d:
                    schema["description"] = d.Description;
                    break;
                case RegularExpressionAttribute r:
                    schema["pattern"] = r.Pattern;
                    break;
                case MinLengthAttribute m:
                    schema["minLength"] = m.Length;
                    break;
                case RangeAttribute r:
                    schema["minimum"] = JsonValue.Create(Convert.ToInt64(r.Minimum, System.Globalization.CultureInfo.InvariantCulture));
                    if (!IsOpenMaximum(r.Maximum))
                    {
                        schema["maximum"] = JsonValue.Create(Convert.ToInt64(r.Maximum, System.Globalization.CultureInfo.InvariantCulture));
                    }

                    break;
                case ValidationAttribute other:
                    throw new NotSupportedException(
                        $"'{jsonName}' uses [{other.GetType().Name}], which {nameof(ContractSchemaGenerator)} cannot put in a schema yet; add a mapping.");
            }
        }
    }

    private static IEnumerable<Attribute> CommonFieldAttributes(string jsonName) =>
        typeof(IIntegrationEvent).GetProperties()
            .Where(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name) == jsonName)
            .SelectMany(p => p.GetCustomAttributes(inherit: true).OfType<Attribute>());

    private static bool IsOpenMaximum(object maximum) =>
        maximum is int i ? i == int.MaxValue : maximum is long l && l == long.MaxValue;
}
