using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Contracts.Schema;
using Bank.Messaging.Model;
using Microsoft.EntityFrameworkCore;

namespace Bank.Messaging.Outbox;

/// <summary>
/// Turns an event into an outbox row in the caller's DbContext (ADR 0012). It never calls SaveChangesAsync: that call
/// would save everything tracked in the shared context, including the caller's unfinished work, in a transaction of its
/// own. The caller saves once when its work is complete (a feature at its end, MessagePipeline after a handler), so the
/// state change and its events are written together or not at all.
/// </summary>
internal sealed class OutboxEventPublisher(DbContext context, TimeProvider timeProvider) : IEventPublisher
{
    public Task PublishAsync<T>(T @event, string key, CancellationToken ct) where T : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        context.EnsureBankMessaging();

        // The runtime type, so that publishing through IIntegrationEvent still finds the record's SchemaUri.
        var eventType = @event.GetType();

        // Only tracked here; the feature's SaveChangesAsync writes it together with the state change, or not at all.
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = @event.EventId,
            SchemaUri = EventSchemaUri.Of(eventType).ToString(),
            MessageKey = key,
            Payload = JsonSerializer.Serialize(@event, eventType, ContractJson.Options),
            OccurredOn = @event.OccurredOn,
            PerformedBy = @event.PerformedBy,
            CreatedAt = timeProvider.GetUtcNow(),
        });

        return Task.CompletedTask;
    }
}
