using Bank.Common;

namespace Bank.Testing;

public sealed class FakeBusinessClock(DateOnly today) : IBusinessClock
{
    public DateOnly Today { get; set; } = today;

    public void AdvanceDays(int days) => Today = Today.AddDays(days);
}
