using System.Globalization;
using System.Text;
using Bank.Contracts.Schema;
using Bank.Messaging.Transport;
using Confluent.Kafka;

namespace Bank.Messaging.Kafka;

/// <summary>
/// The Kafka message format (ADR 0014): key = the entity id, value = the event JSON, both UTF-8; the metadata travels
/// in the headers message-id, schema-uri, performed-by and occurred-on. Keys and values are bytes so the dead-letter
/// topic can keep exactly what arrived, even when it is not valid.
/// </summary>
internal static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string SchemaUri = "schema-uri";
    public const string PerformedBy = "performed-by";
    public const string OccurredOn = "occurred-on";

    private const string DateFormat = "yyyy-MM-dd";

    public static Message<byte[], byte[]> ToKafka(TransportMessage message) => new()
    {
        Key = Encoding.UTF8.GetBytes(message.Key),
        Value = Encoding.UTF8.GetBytes(message.Payload),
        Headers = new Headers
        {
            { MessageId, Encoding.UTF8.GetBytes(message.MessageId.ToString("D")) },
            { SchemaUri, Encoding.UTF8.GetBytes(message.SchemaUri) },
            { PerformedBy, Encoding.UTF8.GetBytes(message.PerformedBy) },
            { OccurredOn, Encoding.UTF8.GetBytes(message.OccurredOn.ToString(DateFormat, CultureInfo.InvariantCulture)) },
        },
    };

    /// <summary>Reads a consumed message back; throws <see cref="InvalidMessageException"/> when anything is missing or malformed.</summary>
    public static TransportMessage FromKafka(byte[]? key, byte[]? value, Headers? headers)
    {
        if (key is null || key.Length == 0)
        {
            throw new InvalidMessageException("The message has no key.");
        }

        if (value is null || value.Length == 0)
        {
            throw new InvalidMessageException("The message has no value.");
        }

        if (!Guid.TryParseExact(Read(headers, MessageId), "D", out var messageId))
        {
            throw new InvalidMessageException($"Header {MessageId} is not a GUID.");
        }

        var schemaUri = Read(headers, SchemaUri);
        if (!SchemaKey.TryParse(schemaUri, out _))
        {
            throw new InvalidMessageException($"Header {SchemaUri} is not a schema URI like iglu:se.bank/day_closed/jsonschema/1-0-0.");
        }

        var performedBy = Read(headers, PerformedBy);
        if (string.IsNullOrWhiteSpace(performedBy))
        {
            throw new InvalidMessageException($"Header {PerformedBy} is empty.");
        }

        if (!DateOnly.TryParseExact(Read(headers, OccurredOn), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurredOn))
        {
            throw new InvalidMessageException($"Header {OccurredOn} is not a date like 2026-10-30.");
        }

        return new TransportMessage(messageId, schemaUri, Encoding.UTF8.GetString(key), Encoding.UTF8.GetString(value), performedBy, occurredOn);
    }

    private static string Read(Headers? headers, string name) =>
        headers is not null && headers.TryGetLastBytes(name, out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : throw new InvalidMessageException($"Header {name} is missing.");
}
