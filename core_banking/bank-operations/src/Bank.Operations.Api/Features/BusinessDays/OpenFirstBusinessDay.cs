using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Operations.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bank.Operations.Api.Features.BusinessDays;

/// <summary>
/// At startup, after the migrations: when no business day exists yet, opens Operations:StartDate and publishes
/// BusinessDayOpened, so every service gets its first date through the same event as every later one.
/// </summary>
public sealed class OpenFirstBusinessDay(
    IServiceScopeFactory scopeFactory, IOptions<OperationsOptions> options, TimeProvider time, ILogger<OpenFirstBusinessDay> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        if (await db.BusinessDays.AnyAsync(cancellationToken))
        {
            return;
        }

        var date = options.Value.StartDate;
        var now = time.GetUtcNow();
        db.BusinessDays.Add(new BusinessDay
        {
            Date = date,
            Status = BusinessDayStatus.Open,
            OpenedAt = now,
            CreatedAt = now,
            PerformedBy = PerformedBy.System,
        });
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(
            new BusinessDayOpened(Guid.NewGuid(), date, PerformedBy.System, date), CloseBusinessDay.DayKey(date), cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Opened the first business day {Date}", date);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance starting at the same moment opened it first; its BusinessDayOpened is the only one.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
