namespace Bank.Operations.Api.Logic;

/// <summary>
/// The rules of the end-of-day cycle (design plan ADR 15): a closing day is complete when every configured participant
/// has replied with DayCompleted, and the next business day is the next calendar day (no weekends or holidays in phase 1).
/// </summary>
public static class EndOfDay
{
    /// <summary>
    /// True when every name in <paramref name="participants"/> appears in <paramref name="completions"/>. Services that are
    /// not participants are ignored, and a service that completed twice counts once. Names are compared exactly.
    /// </summary>
    /// <exception cref="ArgumentException">No participants: a configuration error, not a complete day.</exception>
    public static bool IsComplete(IReadOnlyCollection<string> participants, IReadOnlyCollection<string> completions)
    {
        if (participants.Count == 0)
        {
            throw new ArgumentException("At least one end-of-day participant is required.", nameof(participants));
        }

        return participants.All(completions.Contains);
    }

    public static DateOnly NextDate(DateOnly date) => date.AddDays(1);
}
