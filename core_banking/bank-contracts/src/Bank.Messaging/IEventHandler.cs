using Bank.Contracts.Events;

namespace Bank.Messaging;

/// <summary>
/// Reacts to one integration event. The pipeline calls it inside a transaction that already holds the inbox row.
/// Inject the service's DbContext for the handler's own changes and IEventPublisher for follow-up events, and do not
/// call SaveChangesAsync: the pipeline saves and commits after the handler returns, so the effect, the inbox row and
/// any outgoing event commit together. Throw to roll all of it back.
/// </summary>
public interface IEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T @event, CancellationToken ct);
}
