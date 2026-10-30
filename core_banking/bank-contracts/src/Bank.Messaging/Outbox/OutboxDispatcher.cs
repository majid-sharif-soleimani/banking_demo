using Bank.Messaging.Data;
using Bank.Messaging.Transport;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Outbox;

/// <summary>
/// Sends committed outbox rows to the broker (ADR 0006). One call is one poll: lock up to BatchSize unsent rows with
/// FOR UPDATE SKIP LOCKED, send them in order, mark the sent ones. Other dispatcher instances skip the locked rows.
/// A crash between "sent" and "marked" sends the row again on the next poll; the consumer's inbox absorbs that.
/// </summary>
public sealed class OutboxDispatcher(
    IDbConnectionFactory connectionFactory,
    IMessageTransport transport,
    IOptions<BankMessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    private const string LockBatch = """
        SELECT id AS MessageId, schema_uri AS SchemaUri, message_key AS Key, payload::text AS Payload,
               performed_by AS PerformedBy, occurred_on AS OccurredOn
        FROM outbox_message
        WHERE sent_at IS NULL
        ORDER BY position
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """;

    private const string MarkSent = "UPDATE outbox_message SET sent_at = @SentAt WHERE id = ANY(@Ids)";

    private const string RecordFailure = """
        UPDATE outbox_message SET attempts = attempts + 1, last_error = @Error WHERE id = @Id
        """;

    /// <summary>Runs one poll and returns how many messages it sent.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        // Its own unit of work, not part of any request: one poll is one transaction.
        await using var session = new DbSession(connectionFactory);
        await session.BeginAsync(ct);
        var connection = session.Connection;
        var transaction = session.Transaction;

        var batch = (await connection.QueryAsync<TransportMessage>(
            new CommandDefinition(LockBatch, new { options.Value.BatchSize }, transaction, cancellationToken: ct))).ToList();

        var sent = new List<Guid>(batch.Count);
        foreach (var message in batch)
        {
            try
            {
                await transport.SendAsync(message, ct);
                sent.Add(message.MessageId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop at the first failure so that later messages, possibly with the same key, are not sent before it.
                logger.LogWarning(ex, "Sending outbox message {MessageId} ({SchemaUri}) failed; it is retried on the next poll.",
                    message.MessageId, message.SchemaUri);
                await connection.ExecuteAsync(new CommandDefinition(
                    RecordFailure, new { Id = message.MessageId, Error = ex.Message }, transaction, cancellationToken: ct));
                break;
            }
        }

        if (sent.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                MarkSent, new { SentAt = timeProvider.GetUtcNow(), Ids = sent.ToArray() }, transaction, cancellationToken: ct));
        }

        await session.CommitAsync(ct);
        return sent.Count;
    }
}
