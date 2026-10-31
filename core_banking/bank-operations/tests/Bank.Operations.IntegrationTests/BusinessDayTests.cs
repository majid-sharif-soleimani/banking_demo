using System.Net;
using System.Net.Http.Json;
using Bank.Contracts.Events;
using Bank.Messaging.Kafka;
using Bank.Operations.Api.Features.BusinessDays;
using Bank.Operations.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bank.Operations.IntegrationTests;

public class BusinessDayTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day1 = new(2026, 1, 1);
    private static readonly DateOnly Day2 = new(2026, 1, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Startup_opens_the_start_date_and_publishes_BusinessDayOpened()
    {
        await using var app = await OperationsApp.StartAsync(postgres, startDate: new DateOnly(2026, 3, 15));

        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(new DateOnly(2026, 3, 15), "Open"));
        await Eventually.AssertAsync(() =>
        {
            app.Published<BusinessDayOpened>(BusinessDayOpened.SchemaUri).ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 3, 15));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_restart_does_not_open_another_first_day()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using (var first = OperationsApp.StartOn(database))
        {
            await first.OutboxDrainedAsync();
        }

        await using var second = OperationsApp.StartOn(database, startDate: new DateOnly(2030, 1, 1));

        (await database.ScalarAsync<long>("SELECT count(*) FROM business_day")).ShouldBe(1);
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM outbox_message WHERE schema_uri = '{BusinessDayOpened.SchemaUri}'")).ShouldBe(1);
        (await second.GetJsonAsync<BusinessDayResponse>("/business-days/current")).Date.ShouldBe(Day1);
    }

    [Fact]
    public async Task Close_moves_the_day_to_Closing_and_publishes_DayClosed()
    {
        await using var app = await OperationsApp.StartAsync(postgres);

        var response = await app.PostAsync("/business-days/current/close", performedBy: "E002");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<BusinessDayResponse>(Ct)).ShouldBe(new(Day1, "Closing"));
        await Eventually.AssertAsync(() =>
        {
            var closed = app.Published<DayClosed>(DayClosed.SchemaUri).ShouldHaveSingleItem();
            closed.Date.ShouldBe(Day1);
            closed.OccurredOn.ShouldBe(Day1);
            closed.PerformedBy.ShouldBe("E002");
            return Task.CompletedTask;
        });
        app.Broker.Sent.Single(m => m.SchemaUri == DayClosed.SchemaUri).Key.ShouldBe("2026-01-01");
    }

    [Fact]
    public async Task Closing_twice_returns_409_and_publishes_DayClosed_once()
    {
        await using var app = await OperationsApp.StartAsync(postgres);
        await app.PostAsync("/business-days/current/close");

        var again = await app.PostAsync("/business-days/current/close");

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("business_day_not_open");
        await app.OutboxDrainedAsync();
        app.Published<DayClosed>(DayClosed.SchemaUri).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Concurrent_closes_publish_DayClosed_once()
    {
        await using var app = await OperationsApp.StartAsync(postgres);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => app.PostAsync("/business-days/current/close")));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
        await app.OutboxDrainedAsync();
        app.Published<DayClosed>(DayClosed.SchemaUri).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_stub_participant_completes_the_cycle_and_the_next_day_opens()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["stub"]);
        app.ReplyToDayClosed("stub");

        await app.PostAsync("/business-days/current/close");

        await Eventually.AssertAsync(async () =>
            (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day2, "Open")));
        await Eventually.AssertAsync(() =>
        {
            app.Published<BusinessDayOpened>(BusinessDayOpened.SchemaUri).Select(e => e.Date).ShouldBe([Day1, Day2]);
            return Task.CompletedTask;
        });
        (await app.Database.ScalarAsync<string>("SELECT status FROM business_day WHERE date = '2026-01-01'")).ShouldBe("Closed");
        (await app.Database.ScalarAsync<bool>("SELECT closed_at IS NOT NULL FROM business_day WHERE date = '2026-01-01'")).ShouldBeTrue();
    }

    [Fact]
    public async Task Three_days_in_a_row_close_and_open_in_order()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["stub"]);
        app.ReplyToDayClosed("stub");

        foreach (var expected in new[] { Day2, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4) })
        {
            (await app.PostAsync("/business-days/current/close")).StatusCode.ShouldBe(HttpStatusCode.OK);
            await Eventually.AssertAsync(async () =>
                (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(expected, "Open")));
        }

        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM business_day WHERE status = 'Closed'")).ShouldBe(3);
    }

    [Fact]
    public async Task The_day_stays_Closing_until_every_participant_has_completed()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts", "lending"]);
        await app.PostAsync("/business-days/current/close");

        await app.DeliverAsync(StubParticipant.Completed(Day1, "accounts"), DayCompleted.SchemaUri, "2026-01-01");
        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day1, "Closing"));

        await app.DeliverAsync(StubParticipant.Completed(Day1, "lending"), DayCompleted.SchemaUri, "2026-01-01");
        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day2, "Open"));
    }

    [Fact]
    public async Task A_service_that_is_not_a_participant_is_recorded_but_does_not_count()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts"]);
        await app.PostAsync("/business-days/current/close");

        await app.DeliverAsync(StubParticipant.Completed(Day1, "cards"), DayCompleted.SchemaUri, "2026-01-01");

        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day1, "Closing"));
        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM business_day_completion WHERE service = 'cards'")).ShouldBe(1);
    }

    [Fact]
    public async Task DayCompleted_for_another_date_fails_and_changes_nothing()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts"]);
        await app.PostAsync("/business-days/current/close");

        var error = await Should.ThrowAsync<UnexpectedDayCompletedException>(() =>
            app.DeliverAsync(StubParticipant.Completed(new DateOnly(2025, 12, 31), "accounts"), DayCompleted.SchemaUri, "2025-12-31"));

        error.Message.ShouldContain("2025-12-31");
        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM business_day_completion")).ShouldBe(0);
        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM inbox_message")).ShouldBe(0);
        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day1, "Closing"));
    }

    [Fact]
    public async Task Operations_subscribes_to_exactly_the_participants_topics()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts", "lending"]);

        app.Services.GetRequiredService<IOptions<KafkaOptions>>().Value.SubscribeTo.ShouldBe(["accounts", "lending"]);
    }

    [Fact]
    public async Task DayCompleted_while_the_day_is_still_Open_fails()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts"]);

        await Should.ThrowAsync<UnexpectedDayCompletedException>(() =>
            app.DeliverAsync(StubParticipant.Completed(Day1, "accounts"), DayCompleted.SchemaUri, "2026-01-01"));

        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(Day1, "Open"));
    }
}
