namespace Bank.Messaging.Kafka;

/// <summary>
/// The wait before each retry of a failed message (ADR 0013). One first attempt plus one retry per delay, so the
/// default 200 ms, 1 s, 5 s gives four executions before the message goes to the dead-letter topic.
/// </summary>
public static class RetryBackoff
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];

    /// <summary>How long to wait after the given number of failed attempts, or null when no retry is left.</summary>
    public static TimeSpan? DelayAfter(int failedAttempts, IReadOnlyList<TimeSpan> delays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        return failedAttempts <= delays.Count ? delays[failedAttempts - 1] : null;
    }

    public static int MaxExecutions(IReadOnlyList<TimeSpan> delays) => delays.Count + 1;
}
