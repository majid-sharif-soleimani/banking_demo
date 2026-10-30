using System.Text.RegularExpressions;
using Bank.Messaging.Transport;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Kafka;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Makes Kafka the service's IMessageTransport: the producer for its own topic, the topic initializer and, when
    /// Kafka:SubscribeTo lists services, the consumer that feeds MessagePipeline. Call it next to AddBankMessaging,
    /// which provides the service name. The broker address comes from ConnectionStrings:kafka.
    /// </summary>
    public static IServiceCollection AddKafkaTransport(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.Section))
            .Configure(o => o.BootstrapServers = configuration.GetConnectionString(KafkaOptions.ConnectionStringName) ?? "")
            // The binder adds configured array items to the defaults instead of replacing them, so the default is applied here.
            .PostConfigure(o => o.RetryDelays = o.RetryDelays.Length == 0 ? [.. RetryBackoff.DefaultDelays] : o.RetryDelays)
            .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers),
                "Kafka: ConnectionStrings:kafka is required. Aspire sets it; otherwise set ConnectionStrings__kafka.")
            .Validate(o => o.SubscribeTo.All(s => ServiceName().IsMatch(s)),
                "Kafka: SubscribeTo lists service names such as \"operations\", not topic names.")
            .Validate(o => o.Partitions > 0 && o.ReplicationFactor > 0, "Kafka: Partitions and ReplicationFactor must be positive.")
            .Validate(o => o.RetryDelays.All(d => d > TimeSpan.Zero), "Kafka: every RetryDelays entry must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProducer<byte[], byte[]>>(sp =>
        {
            var kafka = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
            return new ProducerBuilder<byte[], byte[]>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                ClientId = sp.GetRequiredService<IOptions<BankMessagingOptions>>().Value.ServiceName,
                // Stored on every in-sync replica before ProduceAsync returns; no duplicates from the producer's own retries.
                Acks = Acks.All,
                EnableIdempotence = true,
            }).Build();
        });
        services.AddSingleton<IMessageTransport, KafkaMessageTransport>();
        services.AddSingleton<IHostedService, KafkaTopicInitializer>();
        services.AddHostedService<KafkaConsumerService>();
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9]*$")]
    private static partial Regex ServiceName();
}
