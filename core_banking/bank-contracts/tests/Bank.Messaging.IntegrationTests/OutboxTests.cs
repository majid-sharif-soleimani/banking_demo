using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.IntegrationTests.Support;
using Bank.Messaging.Outbox;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Messaging.IntegrationTests;

public class OutboxTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_saved_with_the_state_change_reaches_the_other_service()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");
        await using var receiver = await TestService.StartAsync(postgres, "receiver", s => s.AddEventHandler<TestEvent, RecordTestEvent>());
        sender.Outbound.Subscribe(receiver.Pipeline.ProcessAsync);
        var @event = TestEvents.New("loan-1");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            AddItem(context);
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);

            // Only tracked so far: nothing is in the database, so no dispatcher can see the row.
            (await sender.Dispatcher.DispatchBatchAsync(Ct)).ShouldBe(0);
            (await sender.CountAsync("outbox_message")).ShouldBe(0);

            await context.SaveChangesAsync(Ct);
        }

        await Eventually.AssertAsync(async () => (await receiver.CountAsync("received_test_event")).ShouldBe(1));
        (await sender.CountAsync("test_item")).ShouldBe(1);

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
            // A feature that needs several SaveChanges in one transaction, and then rolls it back.
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            AddItem(context);
            var @event = TestEvents.New("loan-1");
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
            await context.SaveChangesAsync(Ct);
            await transaction.RollbackAsync(Ct);
        }

        (await sender.Dispatcher.DispatchBatchAsync(Ct)).ShouldBe(0);
        (await sender.CountAsync("test_item")).ShouldBe(0);
        (await sender.CountAsync("outbox_message")).ShouldBe(0);
        sender.Outbound.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Event_without_SaveChanges_is_never_published()
    {
        await using var sender = await TestService.StartAsync(postgres, "sender");

        await using (var scope = sender.Services.CreateAsyncScope())
        {
            AddItem(scope.ServiceProvider.GetRequiredService<TestDbContext>());
            var @event = TestEvents.New("loan-1");
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
            // A feature that throws before SaveChangesAsync ends here: the scope disposes the context.
        }

        (await sender.CountAsync("test_item")).ShouldBe(0);
        (await sender.CountAsync("outbox_message")).ShouldBe(0);
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
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            for (var i = 0; i < count; i++)
            {
                await publisher.PublishAsync(TestEvents.New($"key-{i % 7}", i), $"key-{i % 7}", Ct);
            }

            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(Ct);
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
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, @event.Key, Ct);
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(Ct);
    }

    private static void AddItem(TestDbContext context) =>
        context.TestItems.Add(new TestItem { Id = Guid.NewGuid(), Name = "item", PerformedBy = "tester", CreatedAt = DateTimeOffset.UtcNow });
}
