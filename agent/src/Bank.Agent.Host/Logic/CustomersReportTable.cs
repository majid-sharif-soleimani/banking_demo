using System.Text.Json;

namespace Bank.Agent.Host.Logic;

/// <summary>The customers report as table text, ready to show. <paramref name="More"/>: the bank has more customers than the report holds.</summary>
public sealed record CustomersReport(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool More);

/// <summary>
/// Turns the bank's customers report (the MCP tool host_customers_report) into a table. Pure, so it needs no bank and no
/// model. The columns follow what was asked for; several accounts or loans share one cell, and a dash means none.
/// </summary>
public static class CustomersReportTable
{
    public static CustomersReport Build(string json, bool includeContact, bool includeAccounts, bool includeLoans)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        List<string> columns =
        [
            "Name", "National id",
            .. includeContact ? new[] { "Email", "Phone" } : [],
            .. includeAccounts ? new[] { "Accounts" } : [],
            .. includeLoans ? new[] { "Loans" } : [],
            "Status",
        ];

        var rows = root.GetProperty("customers").EnumerateArray().Select(c => (IReadOnlyList<string>)
        [
            Text(c, "name"), Text(c, "nationalId"),
            .. includeContact ? new[] { Text(c, "email"), Text(c, "phone") } : [],
            .. includeAccounts ? new[] { Joined(c, "accounts", a => $"{Text(a, "number")} ({Text(a, "type")})") } : [],
            .. includeLoans ? new[] { Joined(c, "loans", l => $"{Text(l, "id")} ({Text(l, "productCode")}, {Text(l, "status")})") } : [],
            Text(c, "status"),
        ]).ToList();

        return new CustomersReport(columns, rows, root.GetProperty("more").GetBoolean());
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";

    private static string Joined(JsonElement customer, string list, Func<JsonElement, string> describe)
    {
        var items = customer.GetProperty(list).EnumerateArray().Select(describe).ToList();
        return items.Count == 0 ? "-" : string.Join(", ", items);
    }
}
