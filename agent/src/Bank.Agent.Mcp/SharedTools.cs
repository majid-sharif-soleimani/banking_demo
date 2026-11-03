using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp;

/// <summary>Read tools that both the customer and the admin get (see <see cref="ToolSets"/>).</summary>
[McpServerToolType]
public sealed class SharedTools(BankApi bank)
{
    [McpServerTool(Name = "business_date", ReadOnly = true)]
    [Description("The bank's current business date and its status. Use it to turn words like 'today' or 'last month' into dates; the bank's dates are business dates, not the calendar on the wall.")]
    public Task<string> BusinessDateAsync(CancellationToken ct) =>
        bank.GetAsync("/api/operations/business-days/current", ct);

    [McpServerTool(Name = "loan_products_list", ReadOnly = true)]
    [Description("The loan products the bank offers: code, name, currency, repayment type, annual rate, minimum and maximum amount and term in months, penalty and grace rules, and early payoff limits. Amounts are in the product's currency.")]
    public Task<string> LoanProductsAsync(CancellationToken ct) =>
        bank.GetAsync("/api/lending/loan-products", ct);
}
