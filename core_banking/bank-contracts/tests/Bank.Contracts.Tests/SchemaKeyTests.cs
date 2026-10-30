using Bank.Contracts.Schema;

namespace Bank.Contracts.Tests;

public class SchemaKeyTests
{
    [Fact]
    public void Parses_an_iglu_uri()
    {
        var key = SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/1-2-3");

        key.ShouldBe(new SchemaKey("se.bank", "day_closed", "jsonschema", 1, 2, 3));
        key.Version.ShouldBe("1-2-3");
    }

    [Fact]
    public void Formats_back_to_the_same_uri()
    {
        const string uri = "iglu:se.bank/currency_activated/jsonschema/2-0-1";

        SchemaKey.Parse(uri).ToString().ShouldBe(uri);
    }

    [Fact]
    public void Resource_path_follows_the_schema_folder_layout()
    {
        SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/1-0-0").ResourcePath
            .ShouldBe("schemas/se.bank/day_closed/jsonschema/1-0-0.json");
    }

    [Theory]
    [InlineData("se.bank/day_closed/jsonschema/1-0-0")]        // no iglu: prefix
    [InlineData("iglu:se.bank/day_closed/1-0-0")]              // missing format
    [InlineData("iglu:se.bank/day_closed/jsonschema/1-0")]     // two version parts
    [InlineData("iglu:se.bank/day_closed/jsonschema/0-1-0")]   // MODEL starts at 1
    [InlineData("iglu:se.bank/day_closed/jsonschema/1-a-0")]   // not a number
    [InlineData("iglu:se.bank/day_closed/jsonschema/1--1-0")]  // negative
    [InlineData("iglu:se.bank//jsonschema/1-0-0")]             // empty name
    public void Rejects_malformed_uris(string uri)
    {
        SchemaKey.TryParse(uri, out _).ShouldBeFalse();
        Should.Throw<FormatException>(() => SchemaKey.Parse(uri));
    }

    [Fact]
    public void Orders_by_model_then_revision_then_addition()
    {
        var keys = new[] { "2-0-0", "1-1-0", "1-0-10", "1-0-2" }
            .Select(v => SchemaKey.Parse($"iglu:se.bank/x/jsonschema/{v}"))
            .Order()
            .Select(k => k.Version);

        keys.ShouldBe(["1-0-2", "1-0-10", "1-1-0", "2-0-0"]);
    }
}
