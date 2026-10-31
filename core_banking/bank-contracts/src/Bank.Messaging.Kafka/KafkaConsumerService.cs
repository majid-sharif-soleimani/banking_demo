using System.Globalization;
using System.Text;
using Bank.Messaging.Inbox;
using Bank.Messaging.Transport;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Kafka;

/// <summary>
/// Reads the topics in Kafka:SubscribeTo in consumer group bank.&lt;service&gt; and hands each message to
/// <see cref="MessagePipeline"/> (ADR 0013). A failing message is retried in place, which holds up its partition and
/// so keeps the order of every key in it. A permanent failure, or the last failed retry, sends the message to
/// &lt;topic&gt;.dlt. The offset is stored only after the pipeline returned or the dead-letter topic has the message,
/// so a crash at any point means the message is read again, never lost; the inbox makes the second read harmless.
/// </summary>
internal sealed class KafkaConsumerService(
    MessagePipeline pipeline,
    IProducer<byte[], byte[]> producer,
    IOptions<KafkaOptions> kafka,
    IOptions<BankMessagingOptions> messaging,
    TimeProvider timeProvider,
    ILogger<KafkaConsumerService> logger) : BackgroundService
{
    private const int MaxExceptionHeaderLength = 1000;

    private readonly string group = KafkaTopics.ConsumerGroup(messaging.Value.ServiceName);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (kafka.Value.SubscribeTo.Length == 0)
        {
            return Task.CompletedTask;
        }

        // Consume blocks its thread, so the loop gets one of its own and the host can finish starting.
        return Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafka.Value.BootstrapServers,
            GroupId = group,
            ClientId = messaging.Value.ServiceName,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offsets are committed in the background, but only those stored below, after a message is done.
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            // A subscribed topic that is missing from the metadata is looked up again only at the next refresh
            // (5 minutes by default). The initializer waits for its topics; this bounds the wait if one goes missing later.
            TopicMetadataRefreshIntervalMs = 10_000,
        };

        using var consumer = new ConsumerBuilder<byte[], byte[]>(config)
            .SetErrorHandler((_, error) => logger.LogWarning("Kafka consumer error in {Group}: {Reason}", group, error.Reason))
            .Build();
        consumer.Subscribe(kafka.Value.SubscribeTo.Select(KafkaTopics.Events));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<byte[], byte[]> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Consuming in {Group} failed; trying again.", group);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                try
                {
                    await HandleAsync(result, ct);
                    consumer.StoreOffset(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Only a failed dead-letter send gets here. Read the same message again rather than lose it.
                    logger.LogError(ex, "Could not dead-letter {Topic} [{Partition}] @{Offset}; reading it again.",
                        result.Topic, result.Partition.Value, result.Offset.Value);
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(RetryBackoff.DefaultDelays[^1], timeProvider, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping.
        }
        finally
        {
            // Leaves the group at once and commits the stored offsets.
            consumer.Close();
        }
    }

    private async Task HandleAsync(ConsumeResult<byte[], byte[]> result, CancellationToken ct)
    {
        TransportMessage message;
        try
        {
            message = KafkaHeaders.FromKafka(result.Message.Key, result.Message.Value, result.Message.Headers);
        }
        catch (InvalidMessageException ex)
        {
            logger.LogError(ex, "Invalid message at {Topic} [{Partition}] @{Offset}; sending it to the dead-letter topic.",
                result.Topic, result.Partition.Value, result.Offset.Value);
            await DeadLetterAsync(result, ex, DeadLetterHeaders.Reasons.InvalidMessage, attempts: 0, ct);
            return;
        }

        var delays = kafka.Value.RetryDelays;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await pipeline.ProcessAsync(message, ct);
                return;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                if (FailureClassification.IsPermanent(ex))
                {
                    logger.LogError(ex, "Message {MessageId} ({SchemaUri}) can never succeed; sending it to the dead-letter topic.",
                        message.MessageId, message.SchemaUri);
                    await DeadLetterAsync(result, ex, DeadLetterHeaders.Reasons.Permanent, attempt, ct);
                    return;
                }

                if (RetryBackoff.DelayAfter(attempt, delays) is not { } delay)
                {
                    logger.LogError(ex, "Message {MessageId} ({SchemaUri}) failed {Attempts} times; sending it to the dead-letter topic.",
                        message.MessageId, message.SchemaUri, attempt);
                    await DeadLetterAsync(result, ex, DeadLetterHeaders.Reasons.RetriesExhausted, attempt, ct);
                    return;
                }

                logger.LogWarning(ex, "Message {MessageId} ({SchemaUri}) failed, attempt {Attempt} of {MaxAttempts}; retrying in {Delay}.",
                    message.MessageId, message.SchemaUri, attempt, RetryBackoff.MaxExecutions(delays), delay);
                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    /// <summary>Copies the message as it arrived, key, value and headers, to &lt;topic&gt;.dlt with the dlt-* headers added.</summary>
    private async Task DeadLetterAsync(ConsumeResult<byte[], byte[]> result, Exception error, string reason, int attempts, CancellationToken ct)
    {
        var headers = new Headers();
        foreach (var header in result.Message.Headers ?? [])
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        var exception = $"{error.GetType().FullName}: {error.Message}";
        Add(headers, DeadLetterHeaders.Reason, reason);
        Add(headers, DeadLetterHeaders.Exception, exception.Length > MaxExceptionHeaderLength ? exception[..MaxExceptionHeaderLength] : exception);
        Add(headers, DeadLetterHeaders.Attempts, attempts.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.OriginalTopic, result.Topic);
        Add(headers, DeadLetterHeaders.OriginalPartition, result.Partition.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.OriginalOffset, result.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.ConsumerGroup, group);
        Add(headers, DeadLetterHeaders.FailedAt, timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

        await producer.ProduceAsync(
            KafkaTopics.DeadLetter(result.Topic),
            new Message<byte[], byte[]> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers },
            ct);
    }

    private static void Add(Headers headers, string name, string value) => headers.Add(name, Encoding.UTF8.GetBytes(value));
}
