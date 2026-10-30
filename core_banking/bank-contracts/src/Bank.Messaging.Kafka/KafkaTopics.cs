namespace Bank.Messaging.Kafka;

/// <summary>Topic and consumer group names, all derived from service names.</summary>
public static class KafkaTopics
{
    /// <summary>The topic a service publishes all its events to, such as bank.operations.events.</summary>
    public static string Events(string service) => $"bank.{service}.events";

    /// <summary>Where a consumer puts a message it gave up on: the source topic plus .dlt.</summary>
    public static string DeadLetter(string topic) => $"{topic}.dlt";

    /// <summary>One consumer group per service, so its instances share the partitions.</summary>
    public static string ConsumerGroup(string service) => $"bank.{service}";
}
