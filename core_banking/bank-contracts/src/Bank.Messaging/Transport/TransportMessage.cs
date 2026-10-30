namespace Bank.Messaging.Transport;

/// <summary>
/// An event as it travels between services: the JSON payload plus the metadata that becomes Kafka headers
/// (message-id, schema-uri, performed-by, occurred-on). The message id is the event's EventId.
/// </summary>
public sealed record TransportMessage(
    Guid MessageId,
    string SchemaUri,
    string Key,
    string Payload,
    string PerformedBy,
    DateOnly OccurredOn);
