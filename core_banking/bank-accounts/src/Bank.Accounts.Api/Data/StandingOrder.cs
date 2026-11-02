using Bank.Accounts.Api.Logic;

namespace Bank.Accounts.Api.Data;

/// <summary>
/// A recurring transfer. It can be created without enough money; each run needs the full amount at execution, and a
/// run without it is recorded as failed and not retried (design plan section 2).
/// </summary>
public sealed class StandingOrder
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public Guid FromAccountId { get; init; }

    public Guid ToAccountId { get; init; }

    public decimal Amount { get; init; }

    public required string Currency { get; init; }

    public Frequency Frequency { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>The next business date it runs on; null once Cancelled or Completed.</summary>
    public DateOnly? NextRunDate { get; set; }

    public StandingOrderStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }

    public OrderSchedule Schedule => new(Frequency, StartDate, EndDate);
}

public enum StandingOrderStatus
{
    Active,
    Cancelled,
    Completed,
}

/// <summary>One execution of a standing order. (standing order, run date) is unique: a date runs at most once.</summary>
public sealed class StandingOrderRun
{
    public Guid Id { get; init; }

    public Guid StandingOrderId { get; init; }

    public DateOnly RunDate { get; init; }

    public StandingOrderRunStatus Status { get; init; }

    /// <summary>Why it failed, such as insufficient_funds; null on success.</summary>
    public string? Reason { get; init; }

    public Guid? TransferId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public enum StandingOrderRunStatus
{
    Succeeded,
    Failed,
}

/// <summary>Accounts finished its end of day for <see cref="Date"/> and published DayCompleted; at most once per date.</summary>
public sealed class EndOfDayRun
{
    public DateOnly Date { get; init; }

    public int StandingOrdersRun { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}
