using Bank.Operations.Api.Data;
using Bank.Operations.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Operations.IntegrationTests;

public class DataModelTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("currency")]
    [InlineData("business_day")]
    [InlineData("business_day_completion")]
    [InlineData("outbox_message")]
    [InlineData("inbox_message")]
    public async Task Migration_creates_the_table_in_the_service_schema(string table)
    {
        var database = await OperationsDatabase.CreateAsync(postgres);

        (await database.ScalarAsync<bool>($"SELECT to_regclass(current_schema() || '.{table}') IS NOT NULL")).ShouldBeTrue();
    }

    [Fact]
    public async Task Migration_seeds_SEK_EUR_and_USD_inactive_with_two_minor_units()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using var context = database.NewContext();

        var currencies = await context.Currencies.OrderBy(c => c.Code).ToListAsync(Ct);

        currencies.Select(c => c.Code).ShouldBe(["EUR", "SEK", "USD"]);
        currencies.ShouldAllBe(c => !c.IsActive && c.MinorUnits == 2 && c.PerformedBy == "system");
    }

    [Fact]
    public async Task Migration_starts_without_a_business_day()
    {
        // The service opens Operations:StartDate on its first start (slice 2.3a), not the migration.
        var database = await OperationsDatabase.CreateAsync(postgres);

        (await database.ScalarAsync<long>("SELECT count(*) FROM business_day")).ShouldBe(0);
    }

    [Fact]
    public async Task Business_day_status_is_stored_as_text()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using (var context = database.NewContext())
        {
            context.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 1), BusinessDayStatus.Closing));
            await context.SaveChangesAsync(Ct);
        }

        (await database.ScalarAsync<string>("SELECT status FROM business_day")).ShouldBe("Closing");
    }

    [Fact]
    public async Task The_same_date_cannot_be_opened_twice()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using (var first = database.NewContext())
        {
            first.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 2), BusinessDayStatus.Open));
            await first.SaveChangesAsync(Ct);
        }

        await using var second = database.NewContext();
        second.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 2), BusinessDayStatus.Open));

        await ShouldViolateUniqueKeyAsync(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Only_one_business_day_can_be_current()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using (var first = database.NewContext())
        {
            first.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 1), BusinessDayStatus.Closing));
            await first.SaveChangesAsync(Ct);
        }

        await using var second = database.NewContext();
        second.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 2), BusinessDayStatus.Open));

        var error = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));
        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
    }

    [Fact]
    public async Task Closing_a_day_and_opening_the_next_in_one_save_is_allowed()
    {
        // The constraint is checked at commit, so the order of UPDATE and INSERT inside one save does not matter.
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using (var setup = database.NewContext())
        {
            setup.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 1), BusinessDayStatus.Closing));
            await setup.SaveChangesAsync(Ct);
        }

        await using var context = database.NewContext();
        context.BusinessDays.Add(NewDay(new DateOnly(2026, 1, 2), BusinessDayStatus.Open));
        (await context.BusinessDays.SingleAsync(d => d.Date == new DateOnly(2026, 1, 1), Ct)).Status = BusinessDayStatus.Closed;
        await context.SaveChangesAsync(Ct);

        (await database.ScalarAsync<long>("SELECT count(*) FROM business_day WHERE status <> 'Closed'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_service_completes_a_date_only_once()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        var date = new DateOnly(2026, 1, 1);
        await using (var first = database.NewContext())
        {
            first.BusinessDayCompletions.Add(NewCompletion(date, "accounts"));
            await first.SaveChangesAsync(Ct);
        }

        // The same fact again, as a second context would write it for a new message id.
        await using var second = database.NewContext();
        second.BusinessDayCompletions.Add(NewCompletion(date, "accounts"));

        await ShouldViolateUniqueKeyAsync(() => second.SaveChangesAsync(Ct));
        (await database.ScalarAsync<long>("SELECT count(*) FROM business_day_completion")).ShouldBe(1);
    }

    [Fact]
    public async Task Different_services_and_dates_complete_independently()
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        await using var context = database.NewContext();

        context.BusinessDayCompletions.AddRange(
            NewCompletion(new DateOnly(2026, 1, 1), "accounts"),
            NewCompletion(new DateOnly(2026, 1, 1), "lending"),
            NewCompletion(new DateOnly(2026, 1, 2), "accounts"));
        await context.SaveChangesAsync(Ct);

        (await database.ScalarAsync<long>("SELECT count(*) FROM business_day_completion")).ShouldBe(3);
    }

    private static async Task ShouldViolateUniqueKeyAsync(Func<Task> save)
    {
        var error = await Should.ThrowAsync<DbUpdateException>(save);
        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    private static BusinessDay NewDay(DateOnly date, BusinessDayStatus status) => new()
    {
        Date = date,
        Status = status,
        OpenedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        PerformedBy = "tester",
    };

    private static BusinessDayCompletion NewCompletion(DateOnly date, string service) => new()
    {
        Date = date,
        Service = service,
        CompletedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        PerformedBy = "tester",
    };
}
