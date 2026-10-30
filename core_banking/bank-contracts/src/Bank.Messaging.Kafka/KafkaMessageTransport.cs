using Bank.Messaging.Transport;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Kafka;

/// <summary>
/// Sends outbox messages to the service's own topic, bank.&lt;service&gt;.events, keyed by the entity id so that one
/// entity's events keep their order. Returns only when the broker has stored the message (acks=all), so the outbox
/// dispatcher marks a row as sent only when it really is.
/// </summary>
internal sealed class KafkaMessageTransport(IProducer<byte[], byte[]> producer, IOptions<BankMessagingOptions> messaging)
    : IMessageTransport
{
    private readonly string topic = KafkaTopics.Events(messaging.Value.ServiceName);

    public async Task SendAsync(TransportMessage message, CancellationToken ct) =>
        await producer.ProduceAsync(topic, KafkaHeaders.ToKafka(message), ct);
}
