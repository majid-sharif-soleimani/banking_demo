using System.ComponentModel;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.Agent;

/// <summary>
/// Shows a list of customers without the rows going through the model (agent ADR 0009).
///
/// Normally a table is a tool call whose arguments the model writes, so every row is read by the model and written back.
/// That is fine for ten rows and ruinous for a hundred: asking for accounts and loans per customer through the model cost
/// over a million tokens and still never finished. Here the model only decides what the report should hold. The host
/// fetches the rows (host_customers_report, which the model does not get), shows them, and tells the model how many.
/// </summary>
public sealed class CustomersReportTool(AIFunction report, UiTools ui)
{
    public const string Name = "show_customers_report";

    public AIFunction Tool => AIFunctionFactory.Create(ShowAsync, Name,
        "Shows the person a table of customers, sorted by name, with name, national id and status. Use it for any list of customers, " +
        "also with their accounts or loans. Without limit it shows every customer. You do not see the rows, so do not describe them; " +
        "say only that the report is shown.");

    private async Task<string> ShowAsync(
        [Description("Also show email and phone.")] bool includeContact,
        [Description("Also show the numbers of each customer's accounts.")] bool includeAccounts,
        [Description("Also show each customer's loans: the loan id, the product and the status.")] bool includeLoans,
        CancellationToken ct,
        [Description("How many customers to show, when the person asked for a number, such as 5 for 'the first five'. Leave it out for all.")] int? limit = null)
    {
        if (limit is < 1)
        {
            return "The limit must be at least 1. Nothing was shown.";
        }

        var fetched = ToolResult.Read(await report.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["includeAccounts"] = includeAccounts, ["includeLoans"] = includeLoans, ["limit"] = limit }), ct));
        if (!fetched.Done || fetched.Text is null)
        {
            return $"The report could not be read: {fetched.Text}";
        }

        var table = CustomersReportTable.Build(fetched.Text, includeContact, includeAccounts, includeLoans);
        var columns = table.Columns.Select(c => new TableColumn(c, "text")).ToArray();
        ui.Show("show_table", MessageKind.Table, new { title = "Customers", columns, rows = table.Rows });

        if (limit is not null)
        {
            return $"The first {table.Rows.Count} customers by name are shown to the person, as asked.";
        }

        return table.More
            ? $"The report is shown with the first {table.Rows.Count} customers. The bank has more; tell the person the list is not complete."
            : $"The report of {table.Rows.Count} customers is shown to the person.";
    }
}
