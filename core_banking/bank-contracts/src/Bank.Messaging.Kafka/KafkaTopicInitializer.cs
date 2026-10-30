using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Kafka;

/// <summary>
/// Creates the topics this service writes to or reads from, each with its .dlt topic, before any hosted service
/// starts: the outbox dispatcher never sends to a missing topic, and the consumer never subscribes to one.
/// A topic that already exists is left as it is.
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
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
