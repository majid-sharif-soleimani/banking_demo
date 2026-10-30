using Bank.Contracts.Events;
using Bank.Toolkit.Data;

namespace Bank.Messaging;

/// <summary>
/// Reacts to one integration event. The pipeline calls it inside a <see cref="DbSession"/> transaction that already
/// holds the inbox row; inject the DbSession for the handler's own SQL and IEventPublisher for follow-up events,
/// so the effect, the inbox row and any outgoing event commit together. Throw to roll all of it back.
/// </summary>
public interface IEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T @event, CancellationToken ct);
}
