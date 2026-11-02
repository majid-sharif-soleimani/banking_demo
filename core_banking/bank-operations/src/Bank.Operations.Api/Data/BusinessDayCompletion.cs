namespace Bank.Operations.Api.Data;

/// <summary>
/// One DayCompleted reply: <see cref="Service"/> finished its end-of-day work for <see cref="Date"/>. The primary key
/// (date, service) lets a service count once per day, even when it sends the same fact again under a new message id.
/// </summary>
public sealed class BusinessDayCompletion
{
    public DateOnly Date { get; init; }

    public required string Service { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    /// <summary>PostgreSQL's xmin, like every table's Version. Completions are only inserted, so it is never checked.</summary>
    public uint Version { get; set; }
}
