using System.Text.Json.Nodes;
using Bank.Contracts.Tests.Support;

namespace Bank.Contracts.Tests;

/// <summary>
/// Proves the reflection comparer catches drift. Without these, a comparer bug would let every contract test pass.
/// </summary>
public class RecordSchemaComparerTests
{
    private sealed record Sample(Guid Id, DateOnly Day, string Name, string? Note, int Count, Money Price);

    private static JsonObject MatchingSchema() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "id":    { "type": "string", "format": "uuid" },
            "day":   { "type": "string", "format": "date" },
            "name":  { "type": "string" },
            "note":  { "type": ["string", "null"] },
            "count": { "type": "integer" },
            "price": { "type": "object", "properties": { "amount": { "type": "string" }, "currency": { "type": "string" } }, "required": ["amount", "currency"] }
          },
          "required": ["id", "day", "name", "count", "price"]
        }
        """)!.AsObject();

    [Fact]
    public void A_matching_record_and_schema_have_no_problems()
    {
        RecordSchemaComparer.Compare(typeof(Sample), MatchingSchema()).ShouldBeEmpty();
    }

    [Fact]
    public void Reports_a_record_property_missing_from_the_schema()
    {
        var schema = MatchingSchema();
        schema["properties"]!.AsObject().Remove("count");
        schema["required"] = new JsonArray("id", "day", "name", "price");

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'count'") && p.Contains("not in the schema"));
    }

    [Fact]
    public void Reports_a_schema_property_missing_from_the_record()
    {
        var schema = MatchingSchema();
        schema["properties"]!["extra"] = new JsonObject { ["type"] = "string" };

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'extra'") && p.Contains("not in the record"));
    }

    [Fact]
    public void Reports_a_type_mismatch()
    {
        var schema = MatchingSchema();
        schema["properties"]!["count"] = new JsonObject { ["type"] = "string" };

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'count'") && p.Contains("integer"));
    }

    [Fact]
    public void Reports_a_missing_format()
    {
        var schema = MatchingSchema();
        schema["properties"]!["day"] = new JsonObject { ["type"] = "string" };

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'day'") && p.Contains("date"));
    }

    [Fact]
    public void Reports_a_non_nullable_property_that_is_not_required()
    {
        var schema = MatchingSchema();
        schema["required"] = new JsonArray("id", "day", "count", "price");

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'name'") && p.Contains("required"));
    }

    [Fact]
    public void Reports_a_nullable_property_that_is_required()
    {
        var schema = MatchingSchema();
        schema["required"]!.AsArray().Add("note");

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'note'") && p.Contains("required"));
    }

    [Fact]
    public void Reports_a_nullable_property_whose_schema_does_not_allow_null()
    {
        var schema = MatchingSchema();
        schema["properties"]!["note"] = new JsonObject { ["type"] = "string" };

        RecordSchemaComparer.Compare(typeof(Sample), schema).ShouldContain(p => p.Contains("'note'") && p.Contains("null"));
    }

    private sealed record WithDecimal(decimal Amount);

    [Fact]
    public void Rejects_a_bare_decimal_because_money_needs_a_currency()
    {
        var schema = JsonNode.Parse("""{ "type": "object", "properties": { "amount": { "type": "number" } }, "required": ["amount"] }""")!.AsObject();

        RecordSchemaComparer.Compare(typeof(WithDecimal), schema).ShouldContain(p => p.Contains("decimal"));
    }
}
