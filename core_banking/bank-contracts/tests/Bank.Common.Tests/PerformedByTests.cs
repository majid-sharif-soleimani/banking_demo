namespace Bank.Common.Tests;

public class PerformedByTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_actor_becomes_system(string? value)
    {
        PerformedBy.From(value).ShouldBe(PerformedBy.System);
    }

    [Fact]
    public void Actor_is_trimmed()
    {
        PerformedBy.From("  agent-1 ").ShouldBe("agent-1");
    }

    [Fact]
    public void Actor_longer_than_the_limit_is_rejected()
    {
        Should.Throw<ArgumentException>(() => PerformedBy.From(new string('a', PerformedBy.MaxLength + 1)));
    }
}
