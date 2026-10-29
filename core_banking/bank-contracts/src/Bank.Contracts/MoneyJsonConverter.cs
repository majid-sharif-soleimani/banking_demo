using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.Contracts;

/// <summary>
/// Writes Money as { "amount": "1234.50", "currency": "SEK" }. The amount is a string so that
/// consumers whose JSON parser uses binary floating point (JavaScript, many others) cannot lose precision.
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const NumberStyles AmountStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

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

        return decimal.TryParse(reader.GetString(), AmountStyle, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new JsonException("Money amount is not a plain decimal number.");
    }
}
