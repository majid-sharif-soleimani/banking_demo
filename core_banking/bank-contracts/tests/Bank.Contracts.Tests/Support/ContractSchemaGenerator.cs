using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Bank.Contracts.Tests.Support;

/// <summary>
/// Generates the JSON Schema of an event record (ADR 0003). The record is the source of truth: the shape comes from
/// <see cref="JsonSchemaExporter"/> with <see cref="ContractJson.Options"/>, and descriptions and constraints come
/// from attributes on the record, or on <see cref="IIntegrationEvent"/> for the common fields.
/// </summary>
public static class ContractSchemaGenerator
{
    private const string Draft = "https://json-schema.org/draft/2020-12/schema";

    private static readonly Type[] FloatingTypes = [typeof(decimal), typeof(double), typeof(float)];

    // Money has its own converter, so the exporter cannot see its shape; this is what MoneyJsonConverter writes.
    private static JsonObject MoneySchema() => new()
    {
        ["type"] = "object",
        ["description"] = "Money; amount is a decimal string so no consumer loses precision.",
        ["properties"] = new JsonObject
        {
            ["amount"] = new JsonObject { ["type"] = "string", ["pattern"] = @"^-?[0-9]+(\.[0-9]+)?$" },
            ["currency"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Z]{3}$" },
        },
        ["required"] = new JsonArray("amount", "currency"),
        ["additionalProperties"] = false,
    };

    public static JsonObject Generate(Type recordType, SchemaKey key)
    {
        var description = recordType.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"{recordType.Name} needs a [Description] attribute; it becomes the schema description.");

        var exporterOptions = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        var generated = ContractJson.Options.GetJsonSchemaAsNode(recordType, exporterOptions).AsObject();

        // The exporter's TransformSchemaNode callback is skipped for some repeated types (a second Money field),
        // so Money and the attributes are applied here, property by property, from the record itself.
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

        var schema = type == typeof(Money)
            ? WithMoneyShape(exported, allowsNull: nullability.Create(property).ReadState == NullabilityState.Nullable)
            : exported as JsonObject ?? throw new NotSupportedException(
                $"'{jsonName}' of type {type.Name} has no JSON shape the generator understands; contracts hold flat fields and Money.");

        ApplyAttributes(schema, jsonName, property);
        return schema;
    }

    private static JsonObject WithMoneyShape(JsonNode? exported, bool allowsNull)
    {
        var money = MoneySchema();
        if (allowsNull)
        {
            money["type"] = new JsonArray("object", "null");
        }

        if (exported is JsonObject original && original.TryGetPropertyValue("default", out var defaultValue))
        {
            money["default"] = defaultValue?.DeepClone();
        }

        return money;
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
