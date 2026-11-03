using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class SessionEndTests
{
    [Theory]
    [InlineData("session_expired", ChatCloseReason.SessionExpired)]
    [InlineData("session_replaced", ChatCloseReason.SessionReplaced)]
    // Identity answers the explaining 401 once and then forgets the token (ADR 0007): later uses say invalid_token.
    [InlineData("invalid_token", ChatCloseReason.SessionExpired)]
    [InlineData("missing_token", ChatCloseReason.SessionExpired)]
    public void An_identity_refusal_ends_the_chat_with_a_reason(string code, ChatCloseReason expected) =>
        SessionEnd.FromIdentityCode(code).ShouldBe(expected);

    [Theory]
    [InlineData("invalid_credentials")]
    [InlineData("customers_unavailable")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_codes_do_not_end_a_session(string? code) => SessionEnd.FromIdentityCode(code).ShouldBeNull();

    [Theory]
    [InlineData(ChatCloseReason.Logout, "logout")]
    [InlineData(ChatCloseReason.SessionExpired, "session_expired")]
    [InlineData(ChatCloseReason.SessionReplaced, "session_replaced")]
    [InlineData(ChatCloseReason.NewLogin, "new_login")]
    public void Every_reason_has_the_code_the_ui_and_the_database_use(ChatCloseReason reason, string code) =>
        SessionEnd.Code(reason).ShouldBe(code);

    [Fact]
    public void A_session_is_idle_from_the_timeout_on()
    {
        var lastSeen = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var timeout = TimeSpan.FromMinutes(5);

        SessionEnd.IsIdle(lastSeen, lastSeen + timeout - TimeSpan.FromTicks(1), timeout).ShouldBeFalse();
        SessionEnd.IsIdle(lastSeen, lastSeen + timeout, timeout).ShouldBeTrue();
    }
}
