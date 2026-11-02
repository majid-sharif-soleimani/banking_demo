using Bank.Contracts.Events;
using Bank.Operations.Api.Features.BusinessDays;
using Bank.Operations.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Operations.IntegrationTests;

/// <summary>Opening the next business day exactly once when the last replies are handled at the same moment (ADR 0001).</summary>
public class ExactlyOnceTests(PostgresFixture postgres)
{
    private const int Days = 15;

    [Fact]
    public async Task Two_DayCompleted_messages_at_the_same_time_open_the_next_day_exactly_once()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts", "lending"]);
        var day = new DateOnly(2026, 1, 1);

        // One race per day: a lost race either leaves the day Closing forever or opens the next day twice.
        for (var i = 0; i < Days; i++, day = day.AddDays(1))
        {
            await app.PostAsync("/business-days/current/close");

            await Task.WhenAll(
                Task.Run(() => app.DeliverAsync(StubParticipant.Completed(day, "accounts"), DayCompleted.SchemaUri, $"{day:yyyy-MM-dd}")),
                Task.Run(() => app.DeliverAsync(StubParticipant.Completed(day, "lending"), DayCompleted.SchemaUri, $"{day:yyyy-MM-dd}")));

            (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(day.AddDays(1), "Open"), $"race {i + 1}");
        }

        await app.OutboxDrainedAsync();
        app.Published<BusinessDayOpened>(BusinessDayOpened.SchemaUri).Select(e => e.Date)
            .ShouldBe(Enumerable.Range(0, Days + 1).Select(n => new DateOnly(2026, 1, 1).AddDays(n)));
        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM business_day WHERE status = 'Closed'")).ShouldBe(Days);
    }

    [Fact]
    public async Task A_duplicate_DayCompleted_under_a_new_message_id_has_no_extra_effect()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts", "lending"]);
        var day = new DateOnly(2026, 1, 1);
        await app.PostAsync("/business-days/current/close");

        await app.DeliverAsync(StubParticipant.Completed(day, "accounts"), DayCompleted.SchemaUri, "2026-01-01");
        await app.DeliverAsync(StubParticipant.Completed(day, "accounts"), DayCompleted.SchemaUri, "2026-01-01");

        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(day, "Closing"));
        (await app.Database.ScalarAsync<long>("SELECT count(*) FROM business_day_completion")).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_DayCompleted_message_delivered_twice_is_handled_once()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts"]);
        var completed = StubParticipant.Completed(new DateOnly(2026, 1, 1), "accounts");
        await app.PostAsync("/business-days/current/close");

        await app.DeliverAsync(completed, DayCompleted.SchemaUri, "2026-01-01");
        // A redelivery after the day was closed: the inbox skips it, so it is not "a completion for another day".
        await app.DeliverAsync(completed, DayCompleted.SchemaUri, "2026-01-01");

        await app.OutboxDrainedAsync();
        app.Published<BusinessDayOpened>(BusinessDayOpened.SchemaUri).Count.ShouldBe(2); // the start date and 2026-01-02
    }

    [Fact]
    public async Task A_late_duplicate_after_the_day_closed_is_a_no_op()
    {
        await using var app = await OperationsApp.StartAsync(postgres, participants: ["accounts"]);
        var day = new DateOnly(2026, 1, 1);
        await app.PostAsync("/business-days/current/close");
        await app.DeliverAsync(StubParticipant.Completed(day, "accounts"), DayCompleted.SchemaUri, "2026-01-01");

        // Same fact, new message id, after the day closed: a duplicate, not an error.
        await app.DeliverAsync(StubParticipant.Completed(day, "accounts"), DayCompleted.SchemaUri, "2026-01-01");

        (await app.GetJsonAsync<BusinessDayResponse>("/business-days/current")).ShouldBe(new(day.AddDays(1), "Open"));
    }
}
