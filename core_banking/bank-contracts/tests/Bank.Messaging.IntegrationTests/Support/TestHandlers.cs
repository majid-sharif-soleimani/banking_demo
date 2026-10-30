using Bank.Contracts.Events;

namespace Bank.Messaging.IntegrationTests.Support;

/// <summary>Switches that a test flips to make the handlers below fail or publish.</summary>
public sealed class HandlerBehaviour
{
    public bool FailRecord { get; set; }

    public bool FailAudit { get; set; }

    public bool PublishReply { get; set; }
}

/// <summary>The receiver's main handler: adds the effect, optionally publishes a follow-up event, optionally fails after both.</summary>
public sealed class RecordTestEvent(TestDbContext context, IEventPublisher publisher, HandlerBehaviour behaviour, TimeProvider time)
    : IEventHandler<TestEvent>
{
    public async Task HandleAsync(TestEvent @event, CancellationToken ct)
    {
        context.ReceivedTestEvents.Add(new ReceivedTestEvent
        {
            EventId = @event.EventId,
            MessageKey = @event.Key,
            Sequence = @event.Sequence,
            Amount = @event.Amount.Amount,
            Currency = @event.Amount.Currency,
            PerformedBy = @event.PerformedBy,
            CreatedAt = time.GetUtcNow(),
        });

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
public sealed class AuditTestEvent(TestDbContext context, HandlerBehaviour behaviour, TimeProvider time) : IEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent @event, CancellationToken ct)
    {
        if (behaviour.FailAudit)
        {
            throw new InvalidOperationException("AuditTestEvent failed on purpose.");
        }

        context.AuditedTestEvents.Add(new AuditedTestEvent
        {
            EventId = @event.EventId,
            PerformedBy = @event.PerformedBy,
            CreatedAt = time.GetUtcNow(),
        });
        return Task.CompletedTask;
    }
}
