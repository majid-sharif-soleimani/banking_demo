using Bank.Operations.Api.Logic;

namespace Bank.Operations.UnitTests;

public class EndOfDayTests
{
    private static readonly string[] Participants = ["accounts", "lending"];

    [Fact]
    public void IsComplete_AllParticipantsCompleted_ReturnsTrue() =>
        EndOfDay.IsComplete(Participants, ["accounts", "lending"]).ShouldBeTrue();

    [Fact]
    public void IsComplete_CompletionsInAnotherOrder_ReturnsTrue() =>
        EndOfDay.IsComplete(Participants, ["lending", "accounts"]).ShouldBeTrue();

    [Fact]
    public void IsComplete_OneParticipantMissing_ReturnsFalse() =>
        EndOfDay.IsComplete(Participants, ["accounts"]).ShouldBeFalse();

    [Fact]
    public void IsComplete_NoCompletions_ReturnsFalse() =>
        EndOfDay.IsComplete(Participants, []).ShouldBeFalse();

    [Fact]
    public void IsComplete_UnknownServiceInsteadOfParticipant_ReturnsFalse() =>
        EndOfDay.IsComplete(Participants, ["accounts", "cards"]).ShouldBeFalse();

    [Fact]
    public void IsComplete_UnknownServiceNextToAllParticipants_ReturnsTrue() =>
        EndOfDay.IsComplete(Participants, ["accounts", "cards", "lending"]).ShouldBeTrue();

    [Fact]
    public void IsComplete_DuplicateCompletion_CountsOnce() =>
        EndOfDay.IsComplete(Participants, ["accounts", "accounts"]).ShouldBeFalse();

    [Fact]
    public void IsComplete_NameInOtherCase_DoesNotCount() =>
        // Event contracts only allow lower-case names (^[a-z]+$), so "Accounts" is a different, unknown service.
        EndOfDay.IsComplete(Participants, ["Accounts", "lending"]).ShouldBeFalse();

    [Fact]
    public void IsComplete_SingleParticipantCompleted_ReturnsTrue() =>
        EndOfDay.IsComplete(["accounts"], ["accounts"]).ShouldBeTrue();

    [Fact]
    public void IsComplete_NoParticipants_Throws() =>
        // Otherwise "all of none have completed" would be true and every day would close without any end-of-day work.
        Should.Throw<ArgumentException>(() => EndOfDay.IsComplete([], ["accounts"]));

    [Theory]
    [InlineData("2026-01-01", "2026-01-02")]
    [InlineData("2026-01-31", "2026-02-01")] // month end
    [InlineData("2026-02-28", "2026-03-01")] // February in a common year
    [InlineData("2028-02-28", "2028-02-29")] // leap day exists in 2028
    [InlineData("2028-02-29", "2028-03-01")]
    [InlineData("2026-12-31", "2027-01-01")] // year end
    [InlineData("2026-10-31", "2026-11-01")] // a Saturday: weekends are business days in phase 1
    public void NextDate_AnyDate_ReturnsNextCalendarDay(string date, string expected) =>
        EndOfDay.NextDate(DateOnly.Parse(date)).ShouldBe(DateOnly.Parse(expected));
}
