using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp;

/// <summary>
/// The customer's read tools (phase 2 design plan, section 2.2, the "Simple" customer rows).
///
/// What is a tool? A function the language model may ask to run. The model never runs code itself: it answers "call
/// accounts_list", the agent host runs the call through this MCP server, and the result goes back to the model as
/// text. The name, the description and the parameter descriptions below are all the model knows about a tool, so they
/// are written for the model: what the tool returns and when to use it.
///
/// Each tool is one bank call, without business logic. The customer id is never a parameter: it comes from the MCP
/// connection (<see cref="BankApi.CustomerId"/>), so the model cannot ask for another customer's data.
/// </summary>
[McpServerToolType]
public sealed class CustomerTools(BankApi bank)
{
    [McpServerTool(Name = "business_date", ReadOnly = true)]
    [Description("The bank's current business date and its status. Use it to turn words like 'today' or 'last month' into dates; the bank's dates are business dates, not the calendar on the wall.")]
    public Task<string> BusinessDateAsync(CancellationToken ct) =>
        bank.GetAsync("/api/operations/business-days/current", ct);

    [McpServerTool(Name = "accounts_list", ReadOnly = true)]
    [Description("The logged-in customer's accounts: id, number, type, currency, balance (amount and currency) and status.")]
    public Task<string> AccountsAsync(CancellationToken ct) =>
        bank.GetAsync($"/api/accounts/accounts?customerId={bank.CustomerId}", ct);

    [McpServerTool(Name = "account_statement", ReadOnly = true)]
    [Description("The statement of one of the customer's accounts between two business dates: opening balance, every entry with its balance after, and closing balance. 'to' must not be after the current business date.")]
    public Task<string> StatementAsync(
        [Description("The account id (a GUID from accounts_list), not the account number.")] Guid accountId,
        [Description("First business date, yyyy-MM-dd.")] DateOnly from,
        [Description("Last business date, yyyy-MM-dd.")] DateOnly to,
        CancellationToken ct) =>
        bank.GetAsync($"/api/accounts/accounts/{accountId}/statement?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    [McpServerTool(Name = "loans_list", ReadOnly = true)]
    [Description("The customer's loans: principal, outstanding principal, overdue amount, days past due, status, classification (Current, Overdue, Default) and the full schedule of installments with what is due and what is paid.")]
    public Task<string> LoansAsync(CancellationToken ct) =>
        bank.GetAsync($"/api/lending/loans?customerId={bank.CustomerId}", ct);

    [McpServerTool(Name = "loan_products_list", ReadOnly = true)]
    [Description("The loan products the bank offers: code, name, currency, repayment type, annual rate, minimum and maximum amount and term in months, penalty and grace rules, and early payoff limits. Amounts are in the product's currency.")]
    public Task<string> LoanProductsAsync(CancellationToken ct) =>
        bank.GetAsync("/api/lending/loan-products", ct);
}
