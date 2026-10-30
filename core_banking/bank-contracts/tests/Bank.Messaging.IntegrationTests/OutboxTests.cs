using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.IntegrationTests.Support;
using Bank.Messaging.Outbox;
using Bank.Testing;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Messaging.IntegrationTests;

public class OutboxTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_published_in_a_committed_transaction_reaches_the_other_service()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");
        await using var receiver = await TestService.StartAsync(postgres, "receiver", s => s.AddEventHandler<TestEvent, RecordTestEvent>());
        sender.Outbound.Subscribe(receiver.Pipeline.ProcessAsync);
        var @event = TestEvents.New("loan-1");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            await session.BeginAsync(Ct);
            await InsertItemAsync(session);
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);

            // Not committed yet, so no dispatcher can see the row.
            (await sender.Dispatcher.DispatchBatchAsync(Ct)).ShouldBe(0);

            await session.CommitAsync(Ct);
        }

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(1));

        var sent = sender.Outbound.Sent.ShouldHaveSingleItem();
        sent.MessageId.ShouldBe(@event.EventId);
        sent.SchemaUri.ShouldBe(TestEvent.SchemaUri);
        sent.Key.ShouldBe("loan-1");
        sent.PerformedBy.ShouldBe("tester");
        sent.OccurredOn.ShouldBe(@event.OccurredOn);
        JsonSerializer.Deserialize<TestEvent>(sent.Payload, ContractJson.Options).ShouldBe(@event);
        await Eventually.AssertAsync(async () => (await sender.CountAsync("outbox_message", "sent_at IS NOT NULL")).ShouldBe(1));
    }

    [Fact]
    public async Task Rolled_back_transaction_publishes_nothing()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            await session.BeginAsync(Ct);
            await InsertItemAsync(session);
            var @event = TestEvents.New("loan-1");
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
            await session.RollbackAsync(Ct);
        }

        (await sender.Dispatcher.DispatchBatchAsync(Ct)).ShouldBe(0);
        (await sender.CountAsync("test_item")).ShouldBe(0);
        (await sender.CountAsync("outbox_message")).ShouldBe(0);
        sender.Outbound.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disposing_the_session_without_commit_publishes_nothing()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            await session.BeginAsync(Ct);
            var @event = TestEvents.New("loan-1");
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
            // A feature that throws before CommitAsync ends here: the scope disposes the session.
        }

        (await sender.CountAsync("outbox_message")).ShouldBe(0);
    }

    [Fact]
    public async Task Publishing_outside_a_transaction_fails()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");
        await using var scope = sender.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var @event = TestEvents.New("loan-1");

        var error = await Should.ThrowAsync<InvalidOperationException>(() => publisher.PublishAsync(@event, @event.Key, Ct));

        error.Message.ShouldContain("BeginAsync");
    }

    [Fact]
    public async Task Failed_send_stays_in_the_outbox_and_is_sent_on_a_later_poll()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");
        await using var receiver = await TestService.StartAsync(postgres, "receiver", s => s.AddEventHandler<TestEvent, RecordTestEvent>());
        sender.Outbound.Subscribe(receiver.Pipeline.ProcessAsync);
        sender.Outbound.FailSends = true;

        await PublishCommittedAsync(sender, TestEvents.New("loan-1"));

        await Eventually.AssertAsync(async () =>
            (await sender.CountAsync("outbox_message", "sent_at IS NULL AND attempts > 0 AND last_error IS NOT NULL")).ShouldBe(1));
        (await receiver.CountAsync("received_test_event")).ShouldBe(0);

        sender.Outbound.FailSends = false;

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(1));
    }

    [Fact]
    public async Task Concurrent_dispatchers_never_send_the_same_row_twice()
    {
        // Small batches so that the three dispatchers (two here plus the hosted one) compete for rows.
        await using var sender = await TestService.StartAsync(postgres, "sender",
            s => s.Configure<BankMessagingOptions>(o => o.BatchSize = 5));
        const int count = 200;

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await session.BeginAsync(Ct);
            for (var i = 0; i < count; i++)
            {
                await publisher.PublishAsync(TestEvents.New($"key-{i % 7}", i), $"key-{i % 7}", Ct);
            }

            await session.CommitAsync(Ct);
        }

        var extra = Enumerable.Range(0, 2).Select(_ => ActivatorUtilities.CreateInstance<OutboxDispatcher>(sender.Services)).ToList();
        await Task.WhenAll(extra.Select(async dispatcher =>
        {
            while (await dispatcher.DispatchBatchAsync(Ct) > 0)
            {
            }
        }));

        await Eventually.AssertAsync(async () => (await sender.CountAsync("outbox_message", "sent_at IS NULL")).ShouldBe(0));
        sender.Outbound.Sent.Count.ShouldBe(count);
        sender.Outbound.Sent.Select(m => m.MessageId).Distinct().Count().ShouldBe(count);
    }

    private static async Task PublishCommittedAsync(TestService service, TestEvent @event)
    {
        await using var scope = service.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        await session.BeginAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
        await session.CommitAsync(Ct);
    }

    private static Task InsertItemAsync(DbSession session) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO test_item (id, name, performed_by, created_at) VALUES (@Id, 'item', 'tester', now())",
            new { Id = Guid.NewGuid() }, session.Transaction, cancellationToken: Ct));
}
