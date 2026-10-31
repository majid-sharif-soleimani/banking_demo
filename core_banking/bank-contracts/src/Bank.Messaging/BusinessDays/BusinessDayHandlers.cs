using Bank.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace Bank.Messaging.BusinessDays;

/// <summary>
/// Records BusinessDayOpened in the local business_day table. Idempotent: a known date is left as it is, and an older date
/// never moves the clock back, because the clock reads the latest date. The class name is an inbox key: do not rename it.
/// </summary>
public sealed class LocalBusinessDayOpenedHandler(DbContext context, TimeProvider time) : IEventHandler<BusinessDayOpened>
{
    public async Task HandleAsync(BusinessDayOpened @event, CancellationToken ct)
    {
        var days = context.Set<LocalBusinessDay>();
        if (await days.AnyAsync(d => d.Date == @event.Date, ct))
        {
            return;
        }

        days.Add(new LocalBusinessDay
        {
            Date = @event.Date,
            Status = LocalBusinessDayStatus.Open,
            CreatedAt = time.GetUtcNow(),
            PerformedBy = @event.PerformedBy,
        });
    }
}

/// <summary>
/// Marks the date Closed in the local business_day table when Operations closes it. Idempotent. If the opening was never
/// seen (the service started later), the date is recorded as Closed. The class name is an inbox key: do not rename it.
/// </summary>
public sealed class LocalDayClosedHandler(DbContext context, TimeProvider time) : IEventHandler<DayClosed>
{
    public async Task HandleAsync(DayClosed @event, CancellationToken ct)
    {
        var days = context.Set<LocalBusinessDay>();
        var day = await days.SingleOrDefaultAsync(d => d.Date == @event.Date, ct);
        if (day is null)
        {
            days.Add(new LocalBusinessDay
            {
                Date = @event.Date,
                Status = LocalBusinessDayStatus.Closed,
                CreatedAt = time.GetUtcNow(),
                PerformedBy = @event.PerformedBy,
            });
            return;
        }

        if (day.Status != LocalBusinessDayStatus.Closed)
        {
            day.Status = LocalBusinessDayStatus.Closed;
            day.PerformedBy = @event.PerformedBy;
        }
    }
}
