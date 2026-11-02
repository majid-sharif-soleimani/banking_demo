using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Kafka;

/// <summary>
/// Creates the topics this service writes to or reads from, each with its .dlt topic, before any hosted service
/// starts: the outbox dispatcher never sends to a missing topic, and the consumer never subscribes to one.
/// A topic that already exists is left as it is. Creation returns before the broker's metadata shows the new topic, and
/// a consumer that subscribes in that gap only looks again at its next metadata refresh, so this waits until every topic
/// has a leader for each partition.
/// </summary>
internal sealed class KafkaTopicInitializer(IOptions<KafkaOptions> kafka, IOptions<BankMessagingOptions> messaging)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var options = kafka.Value;
        var topics = options.SubscribeTo.Prepend(messaging.Value.ServiceName)
            .Select(KafkaTopics.Events)
            .SelectMany(topic => new[] { topic, KafkaTopics.DeadLetter(topic) })
            .Distinct()
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = options.Partitions,
                ReplicationFactor = options.ReplicationFactor,
            })
            .ToList();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync(topics);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Another instance or service created them first.
        }

        await WaitUntilVisibleAsync(admin, [.. topics.Select(t => t.Name)], cancellationToken);
    }

    private static async Task WaitUntilVisibleAsync(IAdminClient admin, IReadOnlyList<string> topics, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + MetadataWait;
        while (true)
        {
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(5));
            var missing = topics.Where(name => !metadata.Topics.Any(t =>
                t.Topic == name && t.Error.Code == ErrorCode.NoError && t.Partitions.Count > 0 && t.Partitions.All(p => p.Leader >= 0))).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Kafka topics not visible after {MetadataWait.TotalSeconds}s: {string.Join(", ", missing)}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(60);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
