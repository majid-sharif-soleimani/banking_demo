using Bank.Customers.Api.Logic;

namespace Bank.Customers.UnitTests;

public class CustomerRulesTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private static CustomerInput Valid() => new(
        "Ada Lovelace", "19900101-1234", new DateOnly(1990, 1, 1), "ada@example.com", "+46 70 000 00 00", "Example Street 1, Stockholm", "en");

    [Fact]
    public void Validate_ValidInput_HasNoErrors() => CustomerRules.Validate(Valid(), Today).ShouldBeEmpty();

    [Fact]
    public void Validate_MissingLanguage_DefaultsToEnglish() =>
        CustomerRules.LanguageOrDefault(null).ShouldBe("en");

    [Theory]
    [InlineData("en")]
    [InlineData("sv")]
    [InlineData("fa")]
    public void Validate_SupportedLanguage_IsAccepted(string language) =>
        CustomerRules.Validate(Valid() with { PreferredLanguage = language }, Today).ShouldBeEmpty();

    [Theory]
    [InlineData("de")]
    [InlineData("EN")]
    [InlineData("english")]
    public void Validate_UnsupportedLanguage_IsRejected(string language) =>
        CustomerRules.Validate(Valid() with { PreferredLanguage = language }, Today).Keys.ShouldBe(["preferredLanguage"]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankName_IsRejected(string name) =>
        CustomerRules.Validate(Valid() with { Name = name }, Today).Keys.ShouldBe(["name"]);

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@b.se")]
    public void Validate_MalformedEmail_IsRejected(string email) =>
        CustomerRules.Validate(Valid() with { Email = email }, Today).Keys.ShouldBe(["email"]);

    [Fact]
    public void Validate_BirthDateNotBeforeTheBusinessDate_IsRejected() =>
        // Judged against the bank's business date, not the system clock (design plan ADR 14).
        CustomerRules.Validate(Valid() with { DateOfBirth = Today }, Today).Keys.ShouldBe(["dateOfBirth"]);

    [Fact]
    public void Validate_SeveralProblems_AreAllReported() =>
        CustomerRules.Validate(Valid() with { Name = "", NationalId = "", Phone = "", Address = "" }, Today).Keys
            .ShouldBe(["name", "nationalId", "phone", "address"], ignoreOrder: true);

    [Fact]
    public void Validate_TooLongValues_AreRejected() =>
        CustomerRules.Validate(Valid() with { NationalId = new string('1', 33) }, Today).Keys.ShouldBe(["nationalId"]);
}
