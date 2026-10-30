using Bank.Messaging.Model;
using Bank.Messaging.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Outbox;

/// <summary>
/// Sends committed outbox rows to the broker (ADR 0006). One call is one poll: lock up to BatchSize unsent rows with
/// FOR UPDATE SKIP LOCKED, send them in order, mark the sent ones. Other dispatcher instances skip the locked rows.
/// A crash between "sent" and "marked" sends the row again on the next poll; the consumer's inbox absorbs that.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessageTransport transport,
    IOptions<BankMessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>Runs one poll and returns how many messages it sent.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        // Its own scope and context: one poll is its own unit of work, not part of any request.
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        context.EnsureBankMessaging();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // LINQ has no FOR UPDATE SKIP LOCKED; the rows come back tracked, so marking them is a normal SaveChanges.
        var batchSize = options.Value.BatchSize;
        var batch = await context.Set<OutboxMessage>()
            .FromSql($"SELECT * FROM outbox_message WHERE sent_at IS NULL ORDER BY position LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in batch)
        {
            try
            {
                await transport.SendAsync(ToTransportMessage(row), ct);
                row.SentAt = timeProvider.GetUtcNow();
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop at the first failure so that later messages, possibly with the same key, are not sent before it.
                logger.LogWarning(ex, "Sending outbox message {MessageId} ({SchemaUri}) failed; it is retried on the next poll.",
                    row.Id, row.SchemaUri);
                row.Attempts++;
                row.LastError = ex.Message;
                break;
            }
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return sent;
    }

    private static TransportMessage ToTransportMessage(OutboxMessage row) =>
        new(row.Id, row.SchemaUri, row.MessageKey, row.Payload, row.PerformedBy, row.OccurredOn);
}
