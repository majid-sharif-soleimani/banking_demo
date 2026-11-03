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

    [Fact]
    public void A_chart_needs_one_value_per_label_in_every_series()
    {
        UiComponentCheck.Chart("bar", 3, [3, 3]).ShouldBeNull();
        UiComponentCheck.Chart("bar", 3, [3, 2]).ShouldBe("Every series needs one value per label (3).");
        UiComponentCheck.Chart("pie", 3, [3]).ShouldNotBeNull();
        UiComponentCheck.Chart("line", 0, []).ShouldNotBeNull();
        UiComponentCheck.Chart("line", 2, [2, 2, 2, 2, 2]).ShouldBe("A chart can show at most 4 series. Use a table, or several charts.");
    }

    [Fact]
    public void A_diagram_needs_text_of_a_sane_length()
    {
        UiComponentCheck.Diagram("flowchart LR\n A-->B").ShouldBeNull();
        UiComponentCheck.Diagram(" ").ShouldNotBeNull();
        UiComponentCheck.Diagram(new string('a', 5001)).ShouldNotBeNull();
    }
}
