namespace Bank.Common;

/// <summary>
/// The bank's business date, owned by Operations and copied into each service's business_day table from
/// BusinessDayOpened. Business code reads the date only from here, never from the system clock.
/// </summary>
public interface IBusinessClock
{
    DateOnly Today { get; }
}
