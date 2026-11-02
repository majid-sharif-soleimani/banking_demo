namespace Bank.Customers.Api.Logic;

/// <summary>
/// Appointment slots (implementation-decisions, "Appointments"): 60 minutes, starting on the hour from 09:00 to 15:00
/// Europe/Stockholm (the last one ends at 16:00), in person at the bank. Daylight saving is handled by the time zone.
/// </summary>
public static class AppointmentSlots
{
    public const int FirstHour = 9;
    public const int ClosingHour = 16;

    public static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static IReadOnlyList<DateTimeOffset> For(DateOnly date) =>
        [.. Enumerable.Range(FirstHour, ClosingHour - FirstHour).Select(hour => At(date, hour))];

    public static bool IsSlot(DateTimeOffset startsAt)
    {
        var local = TimeZoneInfo.ConvertTime(startsAt, Stockholm);
        return local is { Minute: 0, Second: 0, Millisecond: 0 } && local.Hour >= FirstHour && local.Hour < ClosingHour;
    }

    public static DateOnly LocalDate(DateTimeOffset moment) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, Stockholm).DateTime);

    private static DateTimeOffset At(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, Stockholm.GetUtcOffset(local));
    }
}
