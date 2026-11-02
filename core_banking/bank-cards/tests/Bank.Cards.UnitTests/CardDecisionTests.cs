using Bank.Cards.Api.Logic;

namespace Bank.Cards.UnitTests;

public class CardDecisionTests
{
    [Theory]
    [InlineData(CardBehavior.Approve, true, null)]
    [InlineData(CardBehavior.Decline, false, "declined")]
    [InlineData(CardBehavior.InsufficientFunds, false, "insufficient_funds")]
    public void For_EachTestCard_GivesItsFixedOutcome(CardBehavior behavior, bool captured, string? reason) =>
        CardDecision.For(behavior).ShouldBe(new CardDecision(captured, reason));

    [Theory]
    [InlineData("9999 0000 0000 0001", "9999000000000001")]
    [InlineData("9999-0000-0000-0002", "9999000000000002")]
    [InlineData(" 9999000000000003 ", "9999000000000003")]
    public void Normalize_RemovesSpacesAndDashes(string input, string expected) => CardDecision.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Mask_ShowsOnlyTheLastFourDigits() => CardDecision.Mask("9999000000000001").ShouldBe("************0001");
}
