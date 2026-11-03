using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class FormCheckTests
{
    private static readonly FormField Amount = new("amount", "Amount (SEK)", "number", true, null, 1, 50000);

    private static readonly FormField Date = new("date", "Start date", "date", false, null, null, null);

    private static readonly FormField Frequency = new("frequency", "How often", "choice", true, ["Weekly", "Monthly"], null, null);

    private static readonly FormField[] Form = [Amount, Date, Frequency];

    [Fact]
    public void A_form_with_known_types_and_distinct_names_can_be_shown() => FormCheck.Definition(Form).ShouldBeNull();

    [Fact]
    public void Two_fields_with_one_name_are_refused() => FormCheck.Definition([Amount, Amount]).ShouldNotBeNull();

    [Fact]
    public void A_choice_without_options_is_refused() =>
        FormCheck.Definition([Frequency with { Options = [] }]).ShouldBe("The choice field 'frequency' needs options.");

    [Fact]
    public void An_unknown_type_is_refused() => FormCheck.Definition([Amount with { Type = "file" }]).ShouldNotBeNull();

    [Fact]
    public void Right_values_have_no_errors() =>
        FormCheck.Values(Form, new Dictionary<string, string?> { ["amount"] = "250.50", ["date"] = "2026-12-31", ["frequency"] = "Monthly" }).ShouldBeEmpty();

    [Fact]
    public void A_missing_required_value_and_an_empty_optional_one()
    {
        var errors = FormCheck.Values(Form, new Dictionary<string, string?> { ["frequency"] = "Weekly", ["date"] = "" });

        errors.ShouldBe(new Dictionary<string, string> { ["amount"] = "Required." });
    }

    [Theory]
    [InlineData("abc", "Write a number, such as 1250.50.")]
    [InlineData("0.5", "At least 1.")]
    [InlineData("50000.01", "At most 50000.")]
    public void A_number_is_parsed_and_kept_within_its_limits(string value, string error) =>
        FormCheck.Values([Amount], new Dictionary<string, string?> { ["amount"] = value })["amount"].ShouldBe(error);

    [Fact]
    public void A_date_must_be_yyyy_mm_dd() =>
        FormCheck.Values([Date], new Dictionary<string, string?> { ["date"] = "31/12/2026" })["date"].ShouldBe("Write a date as yyyy-MM-dd.");

    [Fact]
    public void A_choice_must_be_one_of_its_options() =>
        FormCheck.Values([Frequency], new Dictionary<string, string?> { ["frequency"] = "Daily" })["frequency"].ShouldBe("Pick one of the options.");

    [Fact]
    public void A_value_for_a_field_the_form_does_not_have_is_refused() =>
        FormCheck.Values([Date], new Dictionary<string, string?> { ["role"] = "admin" }).Keys.ShouldBe(["role"]);
}
