using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Bank.Contracts.SchemaTool;

namespace Bank.Contracts.Tests;

/// <summary>
/// Proves the generator maps record types and attributes as ADR 0003 says. Without these, a generator bug would be
/// written into every snapshot and the snapshot tests would still pass.
/// </summary>
public class ContractSchemaGeneratorTests
{
    private static readonly SchemaKey Key = SchemaKey.Parse("iglu:se.bank/sample/jsonschema/1-0-0");

    [Description("A sample event.")]
    private sealed record Sample(
        Guid EventId,
        DateOnly OccurredOn,
        string PerformedBy,
        [property: Description("Upper-case code.")][property: RegularExpression("^[A-Z]{3}$")] string Code,
        [property: Range(0, int.MaxValue)] int Count,
        Money Price,
        string? Note = null,
        Money? Fee = null) : IIntegrationEvent;

    private static JsonObject Schema() => ContractSchemaGenerator.Generate(typeof(Sample), Key);

    private static JsonObject Property(string name) => Schema()["properties"]![name]!.AsObject();

    private static List<string> Required() => Schema()["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    [Fact]
    public void Adds_draft_self_and_description_at_the_root()
    {
        var schema = Schema();

        schema["$schema"]!.GetValue<string>().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema["self"]!["name"]!.GetValue<string>().ShouldBe("sample");
        schema["self"]!["version"]!.GetValue<string>().ShouldBe("1-0-0");
        schema["description"]!.GetValue<string>().ShouldBe("A sample event.");
        schema["type"]!.GetValue<string>().ShouldBe("object");
    }

    [Fact]
    public void Maps_guid_and_dateonly_to_string_formats()
    {
        Property("eventId")["format"]!.GetValue<string>().ShouldBe("uuid");
        Property("occurredOn")["format"]!.GetValue<string>().ShouldBe("date");
    }

    [Fact]
    public void Constructor_parameters_without_a_default_are_required_and_optional_ones_are_not()
    {
        Required().ShouldBe(["eventId", "occurredOn", "performedBy", "code", "count", "price"], ignoreOrder: true);
    }

    [Fact]
    public void A_nullable_field_allows_null()
    {
        Property("note")["type"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["string", "null"]);
        Property("fee")["type"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["object", "null"]);
    }

    [Fact]
    public void Money_gets_the_string_amount_shape()
    {
        var price = Property("price");

        price["type"]!.GetValue<string>().ShouldBe("object");
        price["properties"]!["amount"]!["type"]!.GetValue<string>().ShouldBe("string");
        price["properties"]!["currency"]!["pattern"]!.GetValue<string>().ShouldBe("^[A-Z]{3}$");
        price["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Maps_attributes_to_keywords()
    {
        Property("code")["description"]!.GetValue<string>().ShouldBe("Upper-case code.");
        Property("code")["pattern"]!.GetValue<string>().ShouldBe("^[A-Z]{3}$");
        Property("count")["minimum"]!.GetValue<long>().ShouldBe(0);
        Property("count").ContainsKey("maximum").ShouldBeFalse();
    }

    [Fact]
    public void Common_fields_take_their_attributes_from_IIntegrationEvent()
    {
        Property("eventId")["description"]!.GetValue<string>().ShouldContain("inbox key");
        Property("performedBy")["minLength"]!.GetValue<int>().ShouldBe(1);
    }

    [Description("Has a bare decimal.")]
    private sealed record WithDecimal(decimal Amount);

    [Fact]
    public void Rejects_a_bare_decimal_because_money_needs_a_currency()
    {
        Should.Throw<NotSupportedException>(() => ContractSchemaGenerator.Generate(typeof(WithDecimal), Key))
            .Message.ShouldContain("Money");
    }

    private sealed record WithoutDescription(string Name);

    [Fact]
    public void Requires_a_description_on_the_record()
    {
        Should.Throw<InvalidOperationException>(() => ContractSchemaGenerator.Generate(typeof(WithoutDescription), Key))
            .Message.ShouldContain("[Description]");
    }

    [Description("Uses an unmapped attribute.")]
    private sealed record WithUnmappedAttribute([property: EmailAddress] string Email);

    [Fact]
    public void Refuses_a_validation_attribute_it_cannot_map()
    {
        Should.Throw<NotSupportedException>(() => ContractSchemaGenerator.Generate(typeof(WithUnmappedAttribute), Key))
            .Message.ShouldContain("EmailAddress");
    }
}
