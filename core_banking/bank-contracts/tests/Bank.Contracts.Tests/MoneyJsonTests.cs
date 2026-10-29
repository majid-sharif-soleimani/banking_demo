using System.Text.Json;

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
