using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace Bank.Messaging.IntegrationTests.Support;

/// <summary>
/// Reads a topic from the start with a throw-away consumer group, so a test can look at the raw Kafka messages
/// (keys, headers, the dead-letter topic). Test code only; services never touch Confluent.Kafka.
/// </summary>
public static class KafkaTopicReader
{
    public static Task<IReadOnlyList<ConsumeResult<byte[], byte[]>>> ReadAsync(
        string bootstrapServers, string topic, int expected, TimeSpan? timeout = null) =>
        Task.Run<IReadOnlyList<ConsumeResult<byte[], byte[]>>>(() =>
        {
            using var consumer = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"test-reader-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);

            var results = new List<ConsumeResult<byte[], byte[]>>();
            var deadline = timeout ?? TimeSpan.FromSeconds(30);
            var clock = Stopwatch.StartNew();
            while (results.Count < expected && clock.Elapsed < deadline)
            {
                if (consumer.Consume(TimeSpan.FromMilliseconds(200)) is { IsPartitionEOF: false } result)
                {
                    results.Add(result);
                }
            }

            consumer.Close();
            return results;
        });

    public static string Header(this ConsumeResult<byte[], byte[]> result, string name) =>
        Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(name));
}
