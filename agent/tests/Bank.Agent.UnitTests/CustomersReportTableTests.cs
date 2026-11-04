using Bank.Agent.Host.Logic;

namespace Bank.Agent.UnitTests;

public class CustomersReportTableTests
{
    private const string Json = """
        {"customers":[
          {"name":"Ada Berg","nationalId":"IMP-1","email":"ada@example.com","phone":"070","status":"Active",
           "accounts":[{"number":"111","type":"Current"},{"number":"222","type":"Savings"}],
           "loans":[{"id":"0b3c0000-0000-4000-8000-00000000e002","productCode":"CAR","status":"Active"}]},
          {"name":"Bo Lind","nationalId":"IMP-2","email":"bo@example.com","phone":"071","status":"Active","accounts":[],"loans":[]}
        ],"more":false}
        """;

    [Fact]
    public void Name_national_id_and_status_are_always_there()
    {
        var table = CustomersReportTable.Build(Json, includeContact: false, includeAccounts: false, includeLoans: false);

        table.Columns.ShouldBe(["Name", "National id", "Status"]);
        table.Rows[0].ShouldBe(["Ada Berg", "IMP-1", "Active"]);
        table.More.ShouldBeFalse();
    }

    [Fact]
    public void Accounts_and_loans_are_listed_in_one_cell_and_a_dash_means_none()
    {
        var table = CustomersReportTable.Build(Json, includeContact: true, includeAccounts: true, includeLoans: true);

        table.Columns.ShouldBe(["Name", "National id", "Email", "Phone", "Accounts", "Loans", "Status"]);
        table.Rows[0].ShouldBe([
            "Ada Berg", "IMP-1", "ada@example.com", "070", "111 (Current), 222 (Savings)",
            "0b3c0000-0000-4000-8000-00000000e002 (CAR, Active)", "Active"]);
        table.Rows[1][4].ShouldBe("-");
        table.Rows[1][5].ShouldBe("-");
    }

    [Fact]
    public void It_says_when_the_bank_has_more_customers_than_the_report_holds() =>
        CustomersReportTable.Build("""{"customers":[],"more":true}""", false, false, false).More.ShouldBeTrue();
}
