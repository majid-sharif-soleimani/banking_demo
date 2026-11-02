using System.Text.Json;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Contracts.Schema;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Inbox;
using Bank.Messaging.IntegrationTests.Support;
using Bank.Messaging.Transport;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Messaging.IntegrationTests;

public class BusinessDayConsumerTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day1 = new(2026, 1, 1);
    private static readonly DateOnly Day2 = new(2026, 1, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<TestService> StartConsumerAsync() =>
        TestService.StartAsync(postgres, "consumer", services => services.AddBusinessDayConsumer());

    [Fact]
    public async Task Today_is_the_date_of_the_latest_BusinessDayOpened()
    {
        await using var service = await StartConsumerAsync();

        await DeliverAsync(service, Opened(Day1));
        Today(service).ShouldBe(Day1);

        await DeliverAsync(service, Opened(Day2));
        Today(service).ShouldBe(Day2);
    }

    [Fact]
    public async Task An_older_or_repeated_BusinessDayOpened_does_not_move_the_date_back()
    {
        await using var service = await StartConsumerAsync();
        await DeliverAsync(service, Opened(Day2));

        await DeliverAsync(service, Opened(Day1));      // arrives late, from another partition
        await DeliverAsync(service, Opened(Day2));      // the same fact again, new message id

        Today(service).ShouldBe(Day2);
        (await service.CountAsync("business_day")).ShouldBe(2);
    }

    [Fact]
    public async Task DayClosed_marks_the_day_Closed_and_today_stays_until_the_next_day_opens()
    {
        await using var service = await StartConsumerAsync();
        await DeliverAsync(service, Opened(Day1));

        await DeliverAsync(service, Closed(Day1));

        // The participants run their end-of-day work for Day1 while it is Closed; their business date is still Day1.
        Today(service).ShouldBe(Day1);
        (await service.ScalarAsync<string>("SELECT status FROM business_day WHERE date = '2026-01-01'")).ShouldBe("Closed");
    }

    [Fact]
    public async Task DayClosed_for_an_unseen_day_records_it_as_Closed()
    {
        await using var service = await StartConsumerAsync();

        await DeliverAsync(service, Closed(Day1));

        (await service.ScalarAsync<string>("SELECT status FROM business_day")).ShouldBe("Closed");
        Today(service).ShouldBe(Day1);
    }

    [Fact]
    public async Task Today_throws_with_a_hint_before_the_first_BusinessDayOpened()
    {
        await using var service = await StartConsumerAsync();

        Should.Throw<InvalidOperationException>(() => Today(service)).Message.ShouldContain("Kafka:SubscribeTo");
    }

    [Fact]
    public async Task One_scope_keeps_one_date_even_if_a_new_day_opens_meanwhile()
    {
        await using var service = await StartConsumerAsync();
        await DeliverAsync(service, Opened(Day1));
        await using var scope = service.Services.CreateAsyncScope();
        var clock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();
        clock.Today.ShouldBe(Day1);

        await DeliverAsync(service, Opened(Day2));

        clock.Today.ShouldBe(Day1);
        Today(service).ShouldBe(Day2);
    }

    private static DateOnly Today(TestService service)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBusinessClock>().Today;
    }

    private static BusinessDayOpened Opened(DateOnly date) => new(Guid.NewGuid(), date, "system", date);

    private static DayClosed Closed(DateOnly date) => new(Guid.NewGuid(), date, "E001", date);

    private static async Task DeliverAsync<T>(TestService service, T @event) where T : IIntegrationEvent
    {
        var schemaUri = EventSchemaUri.Find(typeof(T))!;
        var outcome = await service.Pipeline.ProcessAsync(
            new TransportMessage(@event.EventId, schemaUri, @event.OccurredOn.ToString("yyyy-MM-dd"),
                JsonSerializer.Serialize(@event, ContractJson.Options), @event.PerformedBy, @event.OccurredOn),
            Ct);
        outcome.ShouldBe(ProcessOutcome.Handled);
    }
}
