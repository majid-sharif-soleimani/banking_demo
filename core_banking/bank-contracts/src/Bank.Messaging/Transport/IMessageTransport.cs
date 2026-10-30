namespace Bank.Messaging.Transport;

/// <summary>
/// Sends outbox messages to the broker. The seam between Bank.Messaging and a broker: Bank.Messaging.Kafka
/// implements it (slice 1.3) and Bank.Testing has an in-memory one. Returning means the broker has the message.
/// On the receiving side the transport hands each message to <see cref="Inbox.MessagePipeline"/>.
/// </summary>
public interface IMessageTransport
{
    Task SendAsync(TransportMessage message, CancellationToken ct);
}
