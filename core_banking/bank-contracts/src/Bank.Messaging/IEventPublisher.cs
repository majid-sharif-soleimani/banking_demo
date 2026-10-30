using Bank.Contracts.Events;

namespace Bank.Messaging;

/// <summary>
/// Publishes an integration event through the outbox: the event is added to the service's DbContext as an outbox row,
/// and written by the same <c>SaveChangesAsync</c> as the feature's own changes. So it is sent only if they are saved.
/// </summary>
public interface IEventPublisher
{
    /// <param name="key">The Kafka message key, the id of the entity the event is about; events with one key keep their order.</param>
    Task PublishAsync<T>(T @event, string key, CancellationToken ct) where T : IIntegrationEvent;
}
