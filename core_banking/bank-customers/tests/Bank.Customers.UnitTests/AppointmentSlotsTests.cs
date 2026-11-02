using Bank.Contracts;
using Bank.Customers.Api.Logic;

namespace Bank.Customers.UnitTests;

public class AppointmentSlotsTests
{
    [Fact]
    public void For_AWinterDay_HasSevenHourlySlotsFrom9To15LocalTime()
    {
        // 09:00–16:00 Europe/Stockholm in 60-minute slots: starts 09:00 … 15:00. In winter Stockholm is UTC+1.
        var slots = AppointmentSlots.For(new DateOnly(2026, 3, 2));

        slots.Count.ShouldBe(7);
        slots[0].ShouldBe(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.FromHours(1)));
        slots[^1].ShouldBe(new DateTimeOffset(2026, 3, 2, 15, 0, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void For_ASummerDay_UsesTheSummerOffset() =>
        // Daylight saving: UTC+2, so 09:00 local is 07:00 UTC.
        AppointmentSlots.For(new DateOnly(2026, 7, 1))[0].UtcDateTime.ShouldBe(new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void For_TheDayClocksGoForward_StillHasSevenSlots() =>
        // 29 Mar 2026, 02:00 → 03:00; the business hours are unaffected.
        AppointmentSlots.For(new DateOnly(2026, 3, 29)).Count.ShouldBe(7);

    [Theory]
    [InlineData(2026, 3, 2, 10, 0, true)]
    [InlineData(2026, 3, 2, 10, 30, false)]  // not on the hour
    [InlineData(2026, 3, 2, 16, 0, false)]   // 16:00 is closing time, not a start
    [InlineData(2026, 3, 2, 8, 0, false)]
    public void IsSlot_ChecksTheLocalHour(int y, int m, int d, int hour, int minute, bool expected) =>
        AppointmentSlots.IsSlot(new DateTimeOffset(y, m, d, hour, minute, 0, TimeSpan.FromHours(1))).ShouldBe(expected);

    [Fact]
    public void LocalDate_IsTheStockholmCalendarDay() =>
        // 23:30 UTC on 1 Mar is 00:30 on 2 Mar in Stockholm.
        AppointmentSlots.LocalDate(new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 3, 2));
}

public class StageFiveEmailTests
{
    [Fact]
    public void InformationRequested_ListsTheItems() =>
        EmailTemplates.InformationRequested("a@b.se", "Ada", "Payslip\nID copy").Body.ShouldContain("- Payslip\n- ID copy");

    [Theory]
    [InlineData("Installment", "installment")]
    [InlineData("Payoff", "early payoff")]
    public void PaymentFailed_NamesWhatFailed(string purpose, string expected) =>
        EmailTemplates.PaymentFailed("a@b.se", "Ada", new Money(6_170.85m, "SEK"), purpose).Body.ShouldContain(expected);

    [Fact]
    public void ClassificationWarning_SaysHowLate() =>
        EmailTemplates.ClassificationWarning("a@b.se", "Ada", "Default", 90).Body.ShouldContain("90 days");

    [Fact]
    public void PaidOffEarly_ShowsTheAmountAndTheWaivedInterest()
    {
        var body = EmailTemplates.PaidOffEarly("a@b.se", "Ada", new Money(197_514.49m, "SEK"), new Money(18_465.20m, "SEK")).Body;

        body.ShouldContain("197,514.49 SEK");
        body.ShouldContain("18,465.20 SEK");
    }
}
