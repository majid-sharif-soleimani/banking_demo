using Bank.Contracts.Events;
using Bank.Messaging.Data;

namespace Bank.Messaging;

/// <summary>
/// Publishes an integration event through the outbox: the event is written to outbox_message inside the
/// current <see cref="DbSession"/> transaction, so it is sent only if the feature's own changes commit.
/// </summary>
public interface IEventPublisher
{
    /// <param name="key">The Kafka message key, the id of the entity the event is about; events with one key keep their order.</param>
    Task PublishAsync<T>(T @event, string key, CancellationToken ct) where T : IIntegrationEvent;
}
