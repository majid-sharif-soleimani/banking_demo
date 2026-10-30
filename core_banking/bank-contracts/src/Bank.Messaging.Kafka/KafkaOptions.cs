namespace Bank.Messaging.Kafka;

/// <summary>
/// Settings from the "Kafka" configuration section; the broker address comes from <c>ConnectionStrings:kafka</c>,
/// which Aspire sets for every service that references the Kafka resource.
/// </summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";
    public const string ConnectionStringName = "kafka";

    /// <summary>Broker addresses, such as localhost:9092. Read from ConnectionStrings:kafka.</summary>
    public string BootstrapServers { get; set; } = "";

    /// <summary>
    /// The services whose events this service reads, such as ["operations"]; each one is the topic
    /// bank.&lt;service&gt;.events (Majid, ADR 0015). Empty for a service that only publishes.
    /// </summary>
    public string[] SubscribeTo { get; set; } = [];

    /// <summary>Partitions of each topic this service creates. More partitions, fewer keys held up by one failing message.</summary>
    public int Partitions { get; set; } = 3;

    /// <summary>Replicas of each topic it creates; 1 for a single local broker.</summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>The wait before each retry of a failed message (ADR 0013). Empty means <see cref="RetryBackoff.DefaultDelays"/>.</summary>
    public TimeSpan[] RetryDelays { get; set; } = [];
}
