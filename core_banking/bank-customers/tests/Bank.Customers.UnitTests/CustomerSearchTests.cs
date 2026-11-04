using Bank.Customers.Api.Logic;

namespace Bank.Customers.UnitTests;

public class CustomerSearchTests
{
    [Theory]
    [InlineData("+46 70 000 00 00", "46700000000")]
    [InlineData("070-000 00 00", "0700000000")]
    [InlineData("(08) 123.45", "0812345")]
    public void PhoneDigits_KeepsOnlyDigits(string phone, string digits) =>
        CustomerSearch.PhoneDigits(phone).ShouldBe(digits);

    [Theory]
    [InlineData("love", @"%love%")]
    [InlineData("  Ada ", @"%Ada%")]
    [InlineData("100%", @"%100\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"c:\x", @"%c:\\x%")]
    public void ContainsPattern_EscapesLikeWildcards(string term, string pattern) =>
        CustomerSearch.ContainsPattern(term).ShouldBe(pattern);

    [Fact]
    public void Validate_NoFilter_ListsEveryCustomer() =>
        CustomerSearch.Validate(new CustomerSearchFilter(null, null, null, null)).ShouldBeEmpty();

    [Theory]
    [InlineData(" ", null, null, null)]
    [InlineData(null, "", null, null)]
    [InlineData(null, null, " ", null)]
    [InlineData(null, null, null, "  ")]
    public void Validate_BlankFilter_IsAnError_SoALostValueNeverListsEveryone(string? name, string? email, string? phone, string? nationalId) =>
        CustomerSearch.Validate(new CustomerSearchFilter(name, email, phone, null, nationalId)).Keys.ShouldBe(["filter"]);

    [Fact]
    public void Validate_TooShortNameAndPhone_AreErrors()
    {
        var errors = CustomerSearch.Validate(new CustomerSearchFilter("A", null, "12", null));

        errors.Keys.Order().ShouldBe(["name", "phone"]);
    }

    [Fact]
    public void Validate_EveryFilterGiven_IsValid() =>
        CustomerSearch.Validate(new CustomerSearchFilter("Ad", "ada@example.com", "070 000", Guid.NewGuid())).ShouldBeEmpty();

    [Fact]
    public void Validate_OnlyNationalId_IsValid() =>
        CustomerSearch.Validate(new CustomerSearchFilter(null, null, null, null, "IMP-C001")).ShouldBeEmpty();
}
