using Bank.Contracts.Events;
using Dapper;
using Bank.Messaging.Data;

namespace Bank.Messaging.IntegrationTests.Support;

/// <summary>Switches that a test flips to make the handlers below fail or publish.</summary>
public sealed class HandlerBehaviour
{
    public bool FailRecord { get; set; }

    public bool FailAudit { get; set; }

    public bool PublishReply { get; set; }
}

/// <summary>The receiver's main handler: writes the effect, optionally publishes a follow-up event, optionally fails after both.</summary>
public sealed class RecordTestEvent(DbSession session, IEventPublisher publisher, HandlerBehaviour behaviour, TimeProvider time)
    : IEventHandler<TestEvent>
{
    public async Task HandleAsync(TestEvent @event, CancellationToken ct)
    {
        await session.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO received_test_event (event_id, message_key, sequence, amount, currency, performed_by, created_at)
            VALUES (@EventId, @Key, @Sequence, @Amount, @Currency, @PerformedBy, @CreatedAt)
            """,
            new
            {
                @event.EventId, @event.Key, @event.Sequence, @event.Amount.Amount, @event.Amount.Currency,
                @event.PerformedBy, CreatedAt = time.GetUtcNow(),
            },
            session.Transaction, cancellationToken: ct));

        if (behaviour.PublishReply)
        {
            var reply = @event with { EventId = Guid.NewGuid(), Key = $"{@event.Key}-reply" };
            await publisher.PublishAsync(reply, reply.Key, ct);
        }

        if (behaviour.FailRecord)
        {
            throw new InvalidOperationException("RecordTestEvent failed on purpose.");
        }
    }
}

/// <summary>A second handler of the same event, to show the inbox key includes the handler name.</summary>
public sealed class AuditTestEvent(DbSession session, HandlerBehaviour behaviour, TimeProvider time) : IEventHandler<TestEvent>
{
    public async Task HandleAsync(TestEvent @event, CancellationToken ct)
    {
        if (behaviour.FailAudit)
        {
            throw new InvalidOperationException("AuditTestEvent failed on purpose.");
        }

        await session.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO audited_test_event (event_id, performed_by, created_at) VALUES (@EventId, @PerformedBy, @CreatedAt)",
            new { @event.EventId, @event.PerformedBy, CreatedAt = time.GetUtcNow() },
            session.Transaction, cancellationToken: ct));
    }
}
