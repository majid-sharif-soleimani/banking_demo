using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bank.Contracts.Schema;

namespace Bank.Contracts;

/// <summary>
/// An amount in one currency. Never rounds on its own: use MoneyRounding (Bank.Common) explicitly.
/// Operations on two different currencies throw <see cref="CurrencyMismatchException"/>.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public sealed record Money(decimal Amount, string Currency) : IComparable<Money>, IJsonSchemaProvider
{
    public string Currency { get; } = ValidateCurrency(Currency);

    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>
    /// The JSON Schema of what <see cref="MoneyJsonConverter"/> writes (<see cref="IJsonSchemaProvider"/>).
    /// </summary>
    public static JsonObject JsonSchema() => JsonNode.Parse("""
        {
          "type": "object",
          "description": "Money; amount is a decimal string so no consumer loses precision.",
          "properties": {
            "amount": { "type": "string", "pattern": "^-?[0-9]+(\\.[0-9]+)?$" },
            "currency": { "type": "string", "pattern": "^[A-Z]{3}$" }
          },
          "required": ["amount", "currency"],
          "additionalProperties": false
        }
        """)!.AsObject();

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor, money.Currency);

    public static Money operator *(decimal factor, Money money) => money * factor;

    public static bool operator <(Money? left, Money? right) => Compare(left, right) < 0;

    public static bool operator >(Money? left, Money? right) => Compare(left, right) > 0;

    public static bool operator <=(Money? left, Money? right) => Compare(left, right) <= 0;

    public static bool operator >=(Money? left, Money? right) => Compare(left, right) >= 0;

    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    // null sorts first, the same order as CompareTo(null)
    private static int Compare(Money? left, Money? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    public override string ToString() => $"{Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    internal static bool IsValidCurrency(string? currency) =>
        currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper);

    private static string ValidateCurrency(string currency) =>
        IsValidCurrency(currency)
            ? currency
            : throw new ArgumentException($"Currency must be a three-letter ISO 4217 code in upper case, got '{currency}'.", nameof(Currency));

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }
}

/// <summary>
/// Writes Money as { "amount": "1234.50", "currency": "SEK" }. The amount is a string so that
/// consumers whose JSON parser uses binary floating point (JavaScript, many others) cannot lose precision.
/// </summary>
public sealed partial class MoneyJsonConverter : JsonConverter<Money>
{
    private const NumberStyles AmountStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    // The same pattern as "amount" in Money.JsonSchema(): decimal.TryParse alone also accepts "+5", ".5" and "5."
    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?$")]
    private static partial Regex AmountPattern();

    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be a JSON object.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "amount":
                    amount = ReadAmount(ref reader);
                    break;
                case "currency":
                    currency = reader.TokenType == JsonTokenType.String
                        ? reader.GetString()
                        : throw new JsonException("Money currency must be a string.");
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (amount is null || currency is null)
        {
            throw new JsonException("Money needs both 'amount' and 'currency'.");
        }

        if (!Money.IsValidCurrency(currency))
        {
            throw new JsonException($"Money currency '{currency}' is not a three-letter upper-case code.");
        }

        return new Money(amount.Value, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("amount", value.Amount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }

    private static decimal ReadAmount(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Money amount must be a string, for example \"1234.50\".");
        }

        var text = reader.GetString()!;
        return AmountPattern().IsMatch(text) && decimal.TryParse(text, AmountStyle, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new JsonException("Money amount is not a plain decimal number.");
    }
}
