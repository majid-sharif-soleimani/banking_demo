using System.Globalization;
using System.Text.Json;
using Json.Schema;

namespace Bank.Contracts.Tests;

public class MoneyJsonTests
{
    [Fact]
    public void Serializes_amount_as_string_and_keeps_its_scale()
    {
        var json = JsonSerializer.Serialize(new Money(1234.50m, "SEK"));

        json.ShouldBe("""{"amount":"1234.50","currency":"SEK"}""");
    }

    [Fact]
    public void Serializes_negative_and_high_precision_amounts_without_exponent()
    {
        JsonSerializer.Serialize(new Money(-0.0000001m, "EUR"))
            .ShouldBe("""{"amount":"-0.0000001","currency":"EUR"}""");
    }

    [Theory]
    [InlineData("1234.50", "SEK")]
    [InlineData("-0.0000001", "EUR")]
    [InlineData("0", "USD")]
    public void What_the_converter_writes_validates_against_Money_JsonSchema(string amount, string currency)
    {
        var schema = JsonSchema.FromText(Money.JsonSchema().ToJsonString());
        var json = JsonSerializer.SerializeToElement(new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency));

        schema.Evaluate(json).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"amount":1234.5,"currency":"SEK"}""")]
    [InlineData("""{"amount":"1234.50","currency":"sek"}""")]
    [InlineData("""{"amount":"1234.50"}""")]
    public void Money_JsonSchema_rejects_what_the_converter_rejects(string json)
    {
        var schema = JsonSchema.FromText(Money.JsonSchema().ToJsonString());

        schema.Evaluate(JsonDocument.Parse(json).RootElement).IsValid.ShouldBeFalse();
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json));
    }

    [Fact]
    public void Ignores_the_naming_policy_of_the_caller()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };

        JsonSerializer.Serialize(new Money(1m, "SEK"), options)
            .ShouldBe("""{"amount":"1","currency":"SEK"}""");
    }

    [Fact]
    public void Round_trips_inside_another_object()
    {
        var original = new Wrapper(new Money(99.90m, "USD"));

        var copy = JsonSerializer.Deserialize<Wrapper>(JsonSerializer.Serialize(original));

        copy.ShouldBe(original);
        copy!.Price.Amount.Scale.ShouldBe((byte)2);
    }

    [Fact]
    public void Reads_properties_in_any_order()
    {
        JsonSerializer.Deserialize<Money>("""{"currency":"SEK","amount":"5.00"}""")
            .ShouldBe(new Money(5m, "SEK"));
    }

    [Theory]
    [InlineData("""{"amount":1234.5,"currency":"SEK"}""")]      // number, not string
    [InlineData("""{"amount":"12,5","currency":"SEK"}""")]      // comma decimal separator
    [InlineData("""{"amount":"1e3","currency":"SEK"}""")]       // exponent
    [InlineData("""{"amount":"10.00"}""")]                      // missing currency
    [InlineData("""{"currency":"SEK"}""")]                      // missing amount
    [InlineData("""{"amount":"10.00","currency":"sek"}""")]     // invalid currency code
    [InlineData("""["10.00","SEK"]""")]                         // not an object
    public void Rejects_invalid_json(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json));
    }

    private sealed record Wrapper(Money Price);
}
