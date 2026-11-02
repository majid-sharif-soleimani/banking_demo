namespace Bank.Messaging.BusinessDays;

/// <summary>
/// A service's copy of one business date, written only from Operations' BusinessDayOpened and DayClosed. Its table is
/// business_day in the service's own schema; IBusinessClock.Today is the latest date in it (ADR 0016).
/// </summary>
public sealed class LocalBusinessDay
{
    public DateOnly Date { get; init; }

    public LocalBusinessDayStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }
}

/// <summary>Open after BusinessDayOpened; Closed after DayClosed, while the service runs its end-of-day work for it.</summary>
public enum LocalBusinessDayStatus
{
    Open,
    Closed,
}
