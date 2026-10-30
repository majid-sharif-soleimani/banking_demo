using System.Text.Json;
using Bank.Messaging.Inbox;

namespace Bank.Messaging.Kafka;

/// <summary>Thrown when a Kafka message lacks a key, a value or one of our headers, or a header cannot be read.</summary>
public sealed class InvalidMessageException(string message) : Exception(message);

/// <summary>
/// Decides whether a failure can ever succeed on retry (ADR 0013). Permanent failures go to the dead-letter topic at
/// once; everything else is treated as transient and retried, including a business-key violation (Majid, ADR 0013).
/// </summary>
public static class FailureClassification
{
    public static bool IsPermanent(Exception error) => error is
        UnsupportedSchemaVersionException   // a MODEL version this service cannot read
        or JsonException                    // the payload does not match the record
        or InvalidMessageException;         // missing or malformed key, value or headers
}
