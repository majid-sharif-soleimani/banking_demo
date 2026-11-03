using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class ChangeSummaryTests
{
    [Fact]
    public void A_known_change_has_a_plain_title() => ChangeSummary.Title("transfer_create").ShouldBe("Transfer money");

    [Fact]
    public void An_unknown_tool_gets_its_name_as_words() => ChangeSummary.Title("standing_order_pause").ShouldBe("Standing order pause");

    [Theory]
    [InlineData("fromAccountId", "From account id")]
    [InlineData("amount", "Amount")]
    [InlineData("scheduleItemNumber", "Schedule item number")]
    public void Argument_names_become_labels(string name, string label) => ChangeSummary.Label(name).ShouldBe(label);

    [Fact]
    public void The_card_shows_every_value_that_will_be_sent_in_order_and_skips_empty_ones()
    {
        var fields = ChangeSummary.Fields([new("amount", "250.50"), new("currency", "SEK"), new("description", null)]);

        fields.ShouldBe([new SummaryField("Amount", "250.50"), new SummaryField("Currency", "SEK")]);
    }
}
