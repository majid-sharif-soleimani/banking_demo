using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Operations.Api.Data;
using Bank.Operations.Api.Logic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Operations.Api.Features.BusinessDays;

/// <summary>
/// Records one participant's DayCompleted. When every configured participant has completed the Closing day, closes it,
/// opens the next calendar day and publishes BusinessDayOpened (design plan ADR 15). The pipeline commits the
/// completion, the day changes and the event together. The class name is part of the inbox key: do not rename it.
/// </summary>
public sealed class DayCompletedHandler(
    OperationsDbContext db, IEventPublisher publisher, IOptions<OperationsOptions> options, TimeProvider time)
    : IEventHandler<DayCompleted>
{
    public async Task HandleAsync(DayCompleted @event, CancellationToken ct)
    {
        // First lock the current day (ADR 0001). Two replies handled at the same moment would otherwise each see only
        // their own completion and both stop (the day stays Closing forever), or both see both and open the next day twice.
        // With the lock, the second handler waits until the first commits and then sees its completion.
        var day = await db.BusinessDays.CurrentForUpdateAsync(ct);

        // The same fact again under a new message id (the inbox only stops the same message id): no extra effect.
        if (await db.BusinessDayCompletions.AnyAsync(c => c.Date == @event.Date && c.Service == @event.Service, ct))
        {
            return;
        }

        if (day is null || day.Date != @event.Date || day.Status != BusinessDayStatus.Closing)
        {
            // Majid, 2026-10-30: a completion for any other day is an error, not a silent no-op. The consumer retries it,
            // then moves it to the dead-letter topic, where it stays visible.
            throw new UnexpectedDayCompletedException(@event, day);
        }

        var now = time.GetUtcNow();
        db.BusinessDayCompletions.Add(new BusinessDayCompletion
        {
            Date = @event.Date,
            Service = @event.Service,
            CompletedAt = now,
            CreatedAt = now,
            PerformedBy = @event.PerformedBy,
        });

        var completed = await db.BusinessDayCompletions.Where(c => c.Date == day.Date).Select(c => c.Service).ToListAsync(ct);
        completed.Add(@event.Service);
        if (!EndOfDay.IsComplete(options.Value.EndOfDayParticipants, completed))
        {
            return;
        }

        day.Status = BusinessDayStatus.Closed;
        day.ClosedAt = now;
        day.PerformedBy = PerformedBy.System;

        var next = EndOfDay.NextDate(day.Date);
        db.BusinessDays.Add(new BusinessDay
        {
            Date = next,
            Status = BusinessDayStatus.Open,
            OpenedAt = now,
            CreatedAt = now,
            PerformedBy = PerformedBy.System,
        });
        await publisher.PublishAsync(new BusinessDayOpened(Guid.NewGuid(), next, PerformedBy.System, next), CloseBusinessDay.DayKey(next), ct);
    }
}

/// <summary>A DayCompleted for a date that is not the current Closing day.</summary>
public sealed class UnexpectedDayCompletedException(DayCompleted completed, BusinessDay? current) : Exception(
    $"DayCompleted from '{completed.Service}' for {completed.Date:yyyy-MM-dd}, but the current business day is " +
    (current is null ? "missing." : $"{current.Date:yyyy-MM-dd} ({current.Status})."));
