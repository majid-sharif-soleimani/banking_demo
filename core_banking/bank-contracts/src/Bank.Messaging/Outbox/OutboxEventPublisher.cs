using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Contracts.Schema;
using Dapper;

namespace Bank.Messaging.Outbox;

internal sealed class OutboxEventPublisher(DbSession session, TimeProvider timeProvider) : IEventPublisher
{
    private const string Insert = """
        INSERT INTO outbox_message (id, schema_uri, message_key, payload, occurred_on, performed_by, created_at)
        VALUES (@Id, @SchemaUri, @Key, @Payload::jsonb, @OccurredOn, @PerformedBy, @CreatedAt)
        """;

    public async Task PublishAsync<T>(T @event, string key, CancellationToken ct) where T : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Without the feature's transaction the event could be sent for a change that never commits.
        var transaction = session.Transaction;

        // The runtime type, so that publishing through IIntegrationEvent still finds the record's SchemaUri.
        var eventType = @event.GetType();
        var command = new CommandDefinition(Insert, new
        {
            Id = @event.EventId,
            SchemaUri = EventSchemaUri.Of(eventType).ToString(),
            Key = key,
            Payload = JsonSerializer.Serialize(@event, eventType, ContractJson.Options),
            @event.OccurredOn,
            @event.PerformedBy,
            CreatedAt = timeProvider.GetUtcNow(),
        }, transaction, cancellationToken: ct);

        await session.Connection.ExecuteAsync(command);
    }
}
