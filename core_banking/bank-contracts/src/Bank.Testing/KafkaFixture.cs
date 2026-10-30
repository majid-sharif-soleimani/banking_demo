using Testcontainers.Kafka;
using Xunit;

namespace Bank.Testing;

/// <summary>
/// One Kafka broker (KRaft, no ZooKeeper) for a test collection. Tests keep apart by using their own service names,
/// so each gets its own topics and consumer groups.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer container = new KafkaBuilder("apache/kafka-native:4.1.0").Build();

    /// <summary>The broker address, for ConnectionStrings:kafka.</summary>
    public string BootstrapServers => container.GetBootstrapAddress();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}
