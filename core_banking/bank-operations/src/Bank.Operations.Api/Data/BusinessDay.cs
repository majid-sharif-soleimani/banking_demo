namespace Bank.Operations.Api.Data;

/// <summary>
/// One business date and where it is in the end-of-day cycle. At most one row is not Closed: the current business day.
/// </summary>
public sealed class BusinessDay
{
    /// <summary>The business date; the primary key, so a date can be opened only once.</summary>
    public DateOnly Date { get; init; }

    public BusinessDayStatus Status { get; set; }

    public DateTimeOffset OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }
}

/// <summary>Open → Closing (DayClosed published) → Closed (every participant sent DayCompleted). Never backwards.</summary>
public enum BusinessDayStatus
{
    Open,
    Closing,
    Closed,
}
