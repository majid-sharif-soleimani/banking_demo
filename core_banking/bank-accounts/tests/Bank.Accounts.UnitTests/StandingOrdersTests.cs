using Bank.Accounts.Api.Logic;

namespace Bank.Accounts.UnitTests;

public class StandingOrdersTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value);

    [Theory]
    [InlineData("2026-03-01", "2026-03-08")]
    [InlineData("2026-03-08", "2026-03-15")]
    [InlineData("2026-03-10", "2026-03-15")] // between two runs: the next scheduled one
    public void NextRunDate_Weekly_IsEverySeventhDayFromTheStart(string after, string expected) =>
        StandingOrders.NextRunDate(new(Frequency.Weekly, D("2026-03-01"), null), D(after)).ShouldBe(D(expected));

    [Theory]
    [InlineData("2026-01-15", "2026-02-15")]
    [InlineData("2026-12-15", "2027-01-15")] // year end
    public void NextRunDate_Monthly_IsTheSameDayNextMonth(string after, string expected) =>
        StandingOrders.NextRunDate(new(Frequency.Monthly, D("2026-01-15"), null), D(after)).ShouldBe(D(expected));

    [Theory]
    [InlineData("2026-01-31", "2026-02-28")] // shorter month: its last day
    [InlineData("2026-02-28", "2026-03-31")] // back to the 31st, not the 28th: counted from the start date
    [InlineData("2026-03-31", "2026-04-30")]
    [InlineData("2028-01-31", "2028-02-29")] // leap year
    public void NextRunDate_MonthlyOnThe31st_UsesTheLastDayOfShorterMonths(string after, string expected)
    {
        var start = D(after).Year == 2028 ? D("2028-01-31") : D("2026-01-31");

        StandingOrders.NextRunDate(new(Frequency.Monthly, start, null), D(after)).ShouldBe(D(expected));
    }

    [Theory]
    [InlineData("2025-11-30", "2026-02-28")]
    [InlineData("2026-02-28", "2026-05-30")]
    [InlineData("2026-05-30", "2026-08-30")]
    public void NextRunDate_Quarterly_IsEveryThirdMonthFromTheStart(string after, string expected) =>
        StandingOrders.NextRunDate(new(Frequency.Quarterly, D("2025-11-30"), null), D(after)).ShouldBe(D(expected));

    [Fact]
    public void NextRunDate_BeforeTheStart_IsTheStartDate() =>
        StandingOrders.NextRunDate(new(Frequency.Monthly, D("2026-04-10"), null), D("2026-03-01")).ShouldBe(D("2026-04-10"));

    [Fact]
    public void NextRunDate_PastTheEndDate_IsNull() =>
        StandingOrders.NextRunDate(new(Frequency.Monthly, D("2026-01-15"), D("2026-03-15")), D("2026-03-15")).ShouldBeNull();

    [Fact]
    public void NextRunDate_OnTheEndDate_IsStillARun() =>
        StandingOrders.NextRunDate(new(Frequency.Monthly, D("2026-01-15"), D("2026-03-15")), D("2026-02-15")).ShouldBe(D("2026-03-15"));

    [Fact]
    public void FirstRunDate_IsTheStartDate() =>
        StandingOrders.FirstRunDate(new(Frequency.Weekly, D("2026-03-05"), null)).ShouldBe(D("2026-03-05"));
}
