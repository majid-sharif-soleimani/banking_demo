using Bank.Identity.Api.Logic;

namespace Bank.Identity.UnitTests;

public class SessionRulesTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static SessionEntry Entry(DateTimeOffset? replacedAt = null) => new("t", new Principal("admin", Roles.Admin), Start, replacedAt);

    [Fact]
    public void Check_NoEntry_IsUnknown() => SessionRules.Check(null, Start, Idle).ShouldBe(SessionState.Unknown);

    [Fact]
    public void Check_JustBeforeTheIdleTimeout_IsValid() =>
        // 10:00 + 5 min = 10:05; one tick earlier is still inside.
        SessionRules.Check(Entry(), Start + Idle - TimeSpan.FromTicks(1), Idle).ShouldBe(SessionState.Valid);

    [Fact]
    public void Check_AtTheIdleTimeout_IsExpired() => SessionRules.Check(Entry(), Start + Idle, Idle).ShouldBe(SessionState.Expired);

    [Fact]
    public void Check_Replaced_IsReplacedEvenWhenAlsoExpired()
    {
        SessionRules.Check(Entry(replacedAt: Start), Start, Idle).ShouldBe(SessionState.Replaced);
        SessionRules.Check(Entry(replacedAt: Start), Start + Idle * 2 - TimeSpan.FromTicks(1), Idle).ShouldBe(SessionState.Replaced);
    }

    [Fact]
    public void ExpiresAt_IsLastSeenPlusTheIdleTimeout() => SessionRules.ExpiresAt(Entry(), Idle).ShouldBe(Start.AddMinutes(5));

    [Fact]
    public void CanForget_AnExpiredSession_OneIdlePeriodAfterItExpired()
    {
        // Expired at 10:05; kept until 10:10 so the first late request still hears "expired".
        SessionRules.CanForget(Entry(), Start.AddMinutes(10).AddTicks(-1), Idle).ShouldBeFalse();
        SessionRules.CanForget(Entry(), Start.AddMinutes(10), Idle).ShouldBeTrue();
    }

    [Fact]
    public void CanForget_AReplacedSession_OneIdlePeriodAfterTheReplacement()
    {
        var replaced = Entry(replacedAt: Start.AddMinutes(2));

        SessionRules.CanForget(replaced, Start.AddMinutes(7).AddTicks(-1), Idle).ShouldBeFalse();
        SessionRules.CanForget(replaced, Start.AddMinutes(7), Idle).ShouldBeTrue();
    }

    [Fact]
    public void CanForget_AValidSession_IsFalse() => SessionRules.CanForget(Entry(), Start.AddMinutes(1), Idle).ShouldBeFalse();
}

public class CredentialsTests
{
    [Theory]
    [InlineData("admin", "admin", true)]
    [InlineData("admin", "Admin", false)]
    [InlineData("Admin", "admin", false)]
    [InlineData("admin", "", false)]
    [InlineData(null, null, false)]
    public void IsAdmin_OnlyTheHardCodedPair_IsAdmin(string? username, string? password, bool expected) =>
        Credentials.IsAdmin(username, password).ShouldBe(expected);

    [Theory]
    [InlineData("IMP-C001", "IMP-C001", "IMP-C001")]
    [InlineData(" IMP-C001 ", "IMP-C001", "IMP-C001")]
    [InlineData("IMP-C001", "IMP-C002", null)]
    [InlineData("IMP-C001", "imp-c001", null)]
    [InlineData("", "", null)]
    [InlineData("  ", "  ", null)]
    [InlineData(null, null, null)]
    [InlineData("admin", "admin", null)]
    public void CustomerNationalId_SameNonBlankValue_IsTheNationalId(string? username, string? password, string? expected) =>
        Credentials.CustomerNationalId(username, password).ShouldBe(expected);

    [Fact]
    public void CustomerNationalId_LongerThanTheStoredMaximum_IsNull()
    {
        var tooLong = new string('9', Credentials.MaxNationalIdLength + 1);

        Credentials.CustomerNationalId(tooLong, tooLong).ShouldBeNull();
    }
}
