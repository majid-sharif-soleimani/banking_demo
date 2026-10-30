using Bank.Messaging.Kafka;

namespace Bank.Messaging.UnitTests;

public class RetryBackoffTests
{
    [Fact]
    public void Default_delays_are_200_ms_1_s_and_5_s()
    {
        RetryBackoff.DefaultDelays.ShouldBe([TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(2, 1_000)]
    [InlineData(3, 5_000)]
    public void Each_failed_attempt_waits_the_next_delay(int failedAttempts, int expectedMilliseconds)
    {
        RetryBackoff.DelayAfter(failedAttempts, RetryBackoff.DefaultDelays).ShouldBe(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public void After_the_fourth_failed_attempt_there_is_no_retry_left()
    {
        // One first attempt plus one retry per delay: four executions, then the dead-letter topic (ADR 0013).
        RetryBackoff.DelayAfter(4, RetryBackoff.DefaultDelays).ShouldBeNull();
    }

    [Fact]
    public void Total_executions_are_one_more_than_the_delays()
    {
        RetryBackoff.MaxExecutions(RetryBackoff.DefaultDelays).ShouldBe(4);
        RetryBackoff.MaxExecutions([]).ShouldBe(1);
    }

    [Fact]
    public void Without_delays_the_first_failure_is_final()
    {
        RetryBackoff.DelayAfter(1, []).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Failed_attempts_start_at_one(int failedAttempts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RetryBackoff.DelayAfter(failedAttempts, RetryBackoff.DefaultDelays));
    }
}
