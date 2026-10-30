namespace Bank.Messaging.Kafka;

/// <summary>
/// Headers a dead-lettered message gets on top of its original ones (ADR 0013): why it was given up, the last error,
/// how often it ran, where it came from and which consumer group gave up on it.
/// </summary>
public static class DeadLetterHeaders
{
    public const string Reason = "dlt-reason";
    public const string Exception = "dlt-exception";
    public const string Attempts = "dlt-attempts";
    public const string OriginalTopic = "dlt-original-topic";
    public const string OriginalPartition = "dlt-original-partition";
    public const string OriginalOffset = "dlt-original-offset";
    public const string ConsumerGroup = "dlt-consumer-group";
    public const string FailedAt = "dlt-failed-at";

    /// <summary>Values of <see cref="Reason"/>.</summary>
    public static class Reasons
    {
        /// <summary>The key, value or headers could not be read; the pipeline never saw it.</summary>
        public const string InvalidMessage = "invalid-message";

        /// <summary>A failure that a retry cannot fix, such as an unknown MODEL version.</summary>
        public const string Permanent = "permanent";

        /// <summary>Every attempt failed.</summary>
        public const string RetriesExhausted = "retries-exhausted";
    }
}
