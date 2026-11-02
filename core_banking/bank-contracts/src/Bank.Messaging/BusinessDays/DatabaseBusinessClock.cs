using Bank.Common;
using Microsoft.EntityFrameworkCore;

namespace Bank.Messaging.BusinessDays;

/// <summary>
/// IBusinessClock for every service except Operations: the latest date in the local business_day table. Scoped, and read
/// once per scope, so one request or one handled message sees one date from start to end, even if a new day opens
/// meanwhile. Throws until the first BusinessDayOpened has arrived.
/// </summary>
internal sealed class DatabaseBusinessClock(DbContext context) : IBusinessClock
{
    private DateOnly? today;

    public DateOnly Today => today ??= context.Set<LocalBusinessDay>().Max(d => (DateOnly?)d.Date)
        ?? throw new InvalidOperationException(
            "No business day is known yet: BusinessDayOpened from Operations has not arrived. Check that Kafka:SubscribeTo lists \"operations\".");
}
