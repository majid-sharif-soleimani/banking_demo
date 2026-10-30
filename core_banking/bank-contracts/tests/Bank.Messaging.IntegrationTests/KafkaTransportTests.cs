using System.Text;
using Bank.Contracts.Events;
using Bank.Messaging.IntegrationTests.Support;
using Bank.Messaging.Kafka;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Messaging.IntegrationTests;

/// <summary>
/// Two services connected through a real Kafka broker. Each test uses its own service names, and so its own topics and
/// consumer groups, which lets the tests share one broker.
/// </summary>
public class KafkaTransportTests(PostgresFixture postgres, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan KafkaTimeout = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name}{Guid.NewGuid():N}"[..(name.Length + 8)];

    private async Task<(TestService Sender, TestService Receiver)> StartPairAsync()
    {
        var senderName = Unique("sender");
        var sender = await TestService.StartWithKafkaAsync(postgres, kafka, senderName, subscribeTo: []);
        var receiver = await TestService.StartWithKafkaAsync(postgres, kafka, Unique("receiver"), subscribeTo: [senderName],
            services => services.AddEventHandler<TestEvent, RecordTestEvent>());
        return (sender, receiver);
    }

    [Fact]
    public async Task Event_saved_with_the_state_change_travels_through_kafka_to_the_other_service()
    {
        var (sender, receiver) = await StartPairAsync();
        await using var _ = sender;
        await using var __ = receiver;
        var @event = TestEvents.New("loan-1");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.TestItems.Add(new TestItem { Id = Guid.NewGuid(), Name = "item", PerformedBy = "tester", CreatedAt = DateTimeOffset.UtcNow });
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
            await context.SaveChangesAsync(Ct);
        }

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(1), KafkaTimeout);

        // The message on the wire: entity id as key, event JSON as value, metadata in the headers (ADR 0014).
        var topic = KafkaTopics.Events(sender.Name);
        var message = (await KafkaTopicReader.ReadAsync(kafka.BootstrapServers, topic, expected: 1)).ShouldHaveSingleItem();
        Encoding.UTF8.GetString(message.Message.Key).ShouldBe("loan-1");
        message.Header(KafkaHeaders.MessageId).ShouldBe(@event.EventId.ToString());
        message.Header(KafkaHeaders.SchemaUri).ShouldBe(TestEvent.SchemaUri);
        message.Header(KafkaHeaders.PerformedBy).ShouldBe("tester");
        message.Header(KafkaHeaders.OccurredOn).ShouldBe("2026-10-30");
    }

    [Fact]
    public async Task Failing_handler_runs_four_times_then_goes_to_the_dead_letter_topic_and_the_partition_moves_on()
    {
        var (sender, receiver) = await StartPairAsync();
        await using var _ = sender;
        await using var __ = receiver;
        var poison = TestEvents.New("loan-1", sequence: 1);
        var next = TestEvents.New("loan-1", sequence: 2);
        receiver.Behaviour.FailWhen = e => e.EventId == poison.EventId;

        await sender.Transport.SendAsync(TestEvents.AsMessage(poison), Ct);
        await sender.Transport.SendAsync(TestEvents.AsMessage(next), Ct);

        // The next message with the same key is handled once the poison message is out of the way.
        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event", "sequence = 2")).ShouldBe(1), KafkaTimeout);
        (await receiver.CountAsync("received_test_event", "sequence = 1")).ShouldBe(0);
        receiver.Behaviour.CallsFor(poison).ShouldBe(4);   // one first attempt and three retries

        var topic = KafkaTopics.Events(sender.Name);
        var dead = (await KafkaTopicReader.ReadAsync(kafka.BootstrapServers, KafkaTopics.DeadLetter(topic), expected: 1)).ShouldHaveSingleItem();
        Encoding.UTF8.GetString(dead.Message.Key).ShouldBe("loan-1");
        dead.Header(KafkaHeaders.MessageId).ShouldBe(poison.EventId.ToString());
        dead.Header(DeadLetterHeaders.Reason).ShouldBe(DeadLetterHeaders.Reasons.RetriesExhausted);
        dead.Header(DeadLetterHeaders.Attempts).ShouldBe("4");
        dead.Header(DeadLetterHeaders.Exception).ShouldContain("RecordTestEvent failed on purpose.");
        dead.Header(DeadLetterHeaders.OriginalTopic).ShouldBe(topic);
        dead.Header(DeadLetterHeaders.ConsumerGroup).ShouldBe(KafkaTopics.ConsumerGroup(receiver.Name));
        (await receiver.CountAsync("inbox_message")).ShouldBe(1);   // only the message that succeeded
    }

    [Fact]
    public async Task Unknown_model_goes_to_the_dead_letter_topic_without_a_retry()
    {
        var (sender, receiver) = await StartPairAsync();
        await using var _ = sender;
        await using var __ = receiver;
        var future = TestEvents.New("loan-1", sequence: 1);
        var next = TestEvents.New("loan-1", sequence: 2);

        await sender.Transport.SendAsync(TestEvents.AsMessage(future, schemaUri: "iglu:se.bank/test_event/jsonschema/2-0-0"), Ct);
        await sender.Transport.SendAsync(TestEvents.AsMessage(next), Ct);

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(1), KafkaTimeout);
        var dead = (await KafkaTopicReader.ReadAsync(
            kafka.BootstrapServers, KafkaTopics.DeadLetter(KafkaTopics.Events(sender.Name)), expected: 1)).ShouldHaveSingleItem();
        dead.Header(DeadLetterHeaders.Reason).ShouldBe(DeadLetterHeaders.Reasons.Permanent);
        dead.Header(DeadLetterHeaders.Attempts).ShouldBe("1");
        receiver.Behaviour.CallsFor(future).ShouldBe(0);   // rejected before any handler ran
    }

    [Fact]
    public async Task Messages_with_the_same_key_are_handled_in_order_even_when_one_is_retried()
    {
        var (sender, receiver) = await StartPairAsync();
        await using var _ = sender;
        await using var __ = receiver;
        var loan1 = Enumerable.Range(1, 10).Select(i => TestEvents.New("loan-1", sequence: i)).ToList();
        var loan2 = Enumerable.Range(1, 10).Select(i => TestEvents.New("loan-2", sequence: i)).ToList();
        var flaky = loan1[2];
        receiver.Behaviour.FailWhen = e => e.EventId == flaky.EventId && receiver.Behaviour.CallsFor(e) <= 2;   // fails twice, then works

        foreach (var (one, two) in loan1.Zip(loan2))
        {
            await sender.Transport.SendAsync(TestEvents.AsMessage(one), Ct);
            await sender.Transport.SendAsync(TestEvents.AsMessage(two), Ct);
        }

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(20), KafkaTimeout);
        const string Expected = "1,2,3,4,5,6,7,8,9,10";
        (await receiver.ScalarAsync<string>(
            "SELECT string_agg(sequence::text, ',' ORDER BY id) FROM received_test_event WHERE message_key = 'loan-1'")).ShouldBe(Expected);
        (await receiver.ScalarAsync<string>(
            "SELECT string_agg(sequence::text, ',' ORDER BY id) FROM received_test_event WHERE message_key = 'loan-2'")).ShouldBe(Expected);
        receiver.Behaviour.CallsFor(flaky).ShouldBe(3);
    }

    [Fact]
    public async Task A_redelivered_message_is_applied_once()
    {
        var (sender, receiver) = await StartPairAsync();
        await using var _ = sender;
        await using var __ = receiver;
        var @event = TestEvents.New("loan-1");
        var marker = TestEvents.New("loan-1", sequence: 2);

        // The outbox sends a message twice when it crashes between "sent" and "marked" (ADR 0006).
        await sender.Transport.SendAsync(TestEvents.AsMessage(@event), Ct);
        await sender.Transport.SendAsync(TestEvents.AsMessage(@event), Ct);
        await sender.Transport.SendAsync(TestEvents.AsMessage(marker), Ct);

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event", "sequence = 2")).ShouldBe(1), KafkaTimeout);
        (await receiver.CountAsync("received_test_event", "sequence = 1")).ShouldBe(1);
        receiver.Behaviour.CallsFor(@event).ShouldBe(1);   // the inbox skipped the second copy before the handler
    }
}
