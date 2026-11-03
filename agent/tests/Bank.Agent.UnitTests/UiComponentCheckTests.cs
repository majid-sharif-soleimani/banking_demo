using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class UiComponentCheckTests
{
    [Fact]
    public void A_table_with_one_cell_per_column_in_every_row_is_shown() =>
        UiComponentCheck.Table(["text", "money"], [["Savings", "1 200.00 SEK"], ["Salary", "50.00 SEK"]]).ShouldBeNull();

    [Fact]
    public void A_table_without_rows_is_shown_because_an_empty_list_is_an_answer() =>
        UiComponentCheck.Table(["text"], []).ShouldBeNull();

    [Fact]
    public void A_row_with_a_missing_cell_is_refused_and_the_model_is_told_which() =>
        UiComponentCheck.Table(["text", "money"], [["Savings", "1 200.00 SEK"], ["Salary"]])
            .ShouldBe("Row 2 has 1 cells, but the table has 2 columns. Give every row one cell per column.");

    [Fact]
    public void A_table_without_columns_is_refused() => UiComponentCheck.Table([], []).ShouldNotBeNull();

    [Fact]
    public void An_unknown_column_type_is_refused() =>
        UiComponentCheck.Table(["currency"], [["SEK"]]).ShouldNotBeNull().ShouldContain("text, money, date, number");

    [Fact]
    public void A_card_needs_a_field()
    {
        UiComponentCheck.Card([]).ShouldNotBeNull();
        UiComponentCheck.Card(["Balance"]).ShouldBeNull();
    }
}
