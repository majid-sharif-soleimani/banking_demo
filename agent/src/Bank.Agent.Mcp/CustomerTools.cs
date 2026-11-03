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
/// The business date, the loan products and the free appointment slots are in <see cref="SharedTools"/>: both roles get them.
/// </summary>
[McpServerToolType]
public sealed class CustomerTools(BankApi bank)
{
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

    [McpServerTool(Name = "standing_orders_list", ReadOnly = true)]
    [Description("The customer's standing orders: id, from and to account ids, amount, frequency, start, end, next run date and status.")]
    public Task<string> StandingOrdersAsync(CancellationToken ct) =>
        bank.GetAsync($"/api/accounts/standing-orders?customerId={bank.CustomerId}", ct);

    [McpServerTool(Name = "loan_simulation", ReadOnly = true)]
    [Description("Simulates a loan of a product: the installment, total interest, total cost and schedule, or the bank's reason when the request is outside the product's limits. Changes nothing.")]
    public Task<string> SimulateAsync(
        [Description("The product code from loan_products_list.")] string productCode,
        [Description("The amount, such as 150000.00.")] decimal amount,
        [Description("The product's currency.")] string currency,
        int termMonths,
        CancellationToken ct) =>
        bank.PostReadAsync($"/api/lending/loan-products/{Uri.EscapeDataString(productCode)}/simulation",
            new { amount = new { amount = amount.ToString(System.Globalization.CultureInfo.InvariantCulture), currency }, termMonths, stages = (object?)null }, ct);

    [McpServerTool(Name = "payoff_quote", ReadOnly = true)]
    [Description("Today's early payoff quote of one loan: outstanding principal, unpaid penalty, interest to date, minimum interest, waived interest and the total.")]
    public Task<string> PayoffQuoteAsync(Guid loanId, CancellationToken ct) =>
        bank.GetAsync($"/api/lending/loans/{loanId}/payoff-quote", ct);

    [McpServerTool(Name = "loan_applications_list", ReadOnly = true)]
    [Description("The customer's loan applications with their status and history (Submitted, UnderReview, Approved, Rejected, Disbursed, Cancelled ...).")]
    public Task<string> ApplicationsAsync(CancellationToken ct) =>
        bank.GetAsync("/api/lending/loan-applications", ct);

    [McpServerTool(Name = "appointments_list", ReadOnly = true)]
    [Description("The customer's appointments: id, application, employee, start time and status.")]
    public Task<string> AppointmentsAsync(CancellationToken ct) =>
        bank.GetAsync($"/api/customers/appointments?customerId={bank.CustomerId}", ct);
}
