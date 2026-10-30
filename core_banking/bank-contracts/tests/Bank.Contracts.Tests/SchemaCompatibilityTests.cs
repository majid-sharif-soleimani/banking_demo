using System.Text.Json.Nodes;
using Bank.Contracts.Schema;
using Bank.Contracts.Tests.Support;

namespace Bank.Contracts.Tests;

public class SchemaCompatibilityTests
{
    private static SchemaKey Key(string version) => SchemaKey.Parse($"iglu:se.bank/sample/jsonschema/{version}");

    private const string V100 = """
        { "type": "object",
          "properties": { "date": { "type": "string", "format": "date" } },
          "required": ["date"] }
        """;

    private const string WithOptionalField = """
        { "type": "object",
          "properties": { "date": { "type": "string", "format": "date" }, "note": { "type": ["string", "null"] } },
          "required": ["date"] }
        """;

    private const string WithRequiredField = """
        { "type": "object",
          "properties": { "date": { "type": "string", "format": "date" }, "note": { "type": "string" } },
          "required": ["date", "note"] }
        """;

    private const string DateRemoved = """
        { "type": "object", "properties": { "day": { "type": "string", "format": "date" } }, "required": ["day"] }
        """;

    private static IReadOnlyList<string> Check(string oldVersion, string oldSchema, string newVersion, string newSchema) =>
        SchemaCompatibility.Check(Key(oldVersion), JsonNode.Parse(oldSchema)!.AsObject(), Key(newVersion), JsonNode.Parse(newSchema)!.AsObject());

    [Fact]
    public void Addition_may_add_an_optional_field()
    {
        Check("1-0-0", V100, "1-0-1", WithOptionalField).ShouldBeEmpty();
    }

    [Fact]
    public void Addition_may_not_add_a_required_field()
    {
        Check("1-0-0", V100, "1-0-1", WithRequiredField).ShouldContain(p => p.Contains("'note'"));
    }

    [Fact]
    public void Addition_may_not_remove_a_field()
    {
        Check("1-0-0", V100, "1-0-1", DateRemoved).ShouldContain(p => p.Contains("'date'"));
    }

    [Fact]
    public void Addition_may_not_change_a_field_type()
    {
        const string changed = """{ "type": "object", "properties": { "date": { "type": "integer" } }, "required": ["date"] }""";

        Check("1-0-0", V100, "1-0-1", changed).ShouldContain(p => p.Contains("'date'"));
    }

    [Fact]
    public void Revision_is_not_used()
    {
        Check("1-0-0", V100, "1-1-0", WithOptionalField).ShouldContain(p => p.Contains("REVISION"));
    }

    [Fact]
    public void Model_may_change_anything()
    {
        Check("1-0-0", V100, "2-0-0", DateRemoved).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1-0-0", "1-0-2")]   // skips 1-0-1
    [InlineData("1-0-3", "1-1-1")]   // revision bump must reset addition
    [InlineData("1-2-0", "2-1-0")]   // model bump must reset revision
    [InlineData("1-0-1", "1-0-0")]   // goes backwards
    public void Versions_must_step_by_exactly_one(string oldVersion, string newVersion)
    {
        Check(oldVersion, V100, newVersion, V100).ShouldContain(p => p.Contains("step"));
    }
}
