using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp;

/// <summary>
/// The customer's change tools (phase 2 design plan, section 2.2, the "Medium" rows; business question 3: every customer
/// change in the first release).
///
/// A change tool is marked ReadOnly = false. That one flag is what the agent host reads to decide that a tool needs the
/// person's confirmation (decision A11: every tool that changes data). The model can ask for a change; the host then
/// shows a confirmation card and runs the tool only after the person presses Confirm. <see cref="BankApi.ChangeAsync"/>
/// refuses any change that arrives without that confirmation's key.
///
/// As in <see cref="CustomerTools"/>, the customer id is never a parameter.
/// </summary>
[McpServerToolType]
public sealed class CustomerChangeTools(BankApi bank)
{
    /// <summary>
    /// The name of the upload tool. The agent host calls it itself when the person uploads a file; the model does not get
    /// it. The <see cref="ToolSets.HostOnlyPrefix"/> is what tells the host so.
    /// </summary>
    public const string DocumentUpload = ToolSets.HostOnlyPrefix + "document_upload";

    [McpServerTool(Name = "transfer_create", ReadOnly = false, Destructive = false)]
    [Description("Moves money from one of the customer's accounts to another account of theirs, in the source account's currency (the bank converts if the target has another currency). Use account ids from accounts_list.")]
    public Task<string> TransferAsync(
        [Description("The id of the account the money leaves.")] Guid fromAccountId,
        [Description("The id of the account the money goes to.")] Guid toAccountId,
        [Description("The amount, such as 250.00.")] decimal amount,
        [Description("The currency of the source account, such as SEK.")] string currency,
        CancellationToken ct,
        [Description("A short text for both statements.")] string? description = null) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/accounts/transfers",
            new { fromAccountId, toAccountId, amount = Money(amount, currency), description }, ct);

    [McpServerTool(Name = "standing_order_create", ReadOnly = false, Destructive = false)]
    [Description("Creates a standing order: a transfer the bank repeats Weekly, Monthly or Quarterly from the start date (not before today's business date) until the optional end date.")]
    public Task<string> CreateStandingOrderAsync(
        Guid fromAccountId,
        Guid toAccountId,
        [Description("The amount of each run, such as 500.00.")] decimal amount,
        [Description("The currency of the source account.")] string currency,
        [Description("Weekly, Monthly or Quarterly.")] string frequency,
        [Description("First run, yyyy-MM-dd.")] DateOnly startDate,
        CancellationToken ct,
        [Description("Last possible run, yyyy-MM-dd, or leave out for no end.")] DateOnly? endDate = null) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/accounts/standing-orders",
            new { fromAccountId, toAccountId, amount = Money(amount, currency), frequency, startDate, endDate }, ct);

    [McpServerTool(Name = "standing_order_cancel", ReadOnly = false, Destructive = true)]
    [Description("Cancels one of the customer's standing orders. Use an id from standing_orders_list.")]
    public Task<string> CancelStandingOrderAsync(Guid standingOrderId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Delete, $"/api/accounts/standing-orders/{standingOrderId}", null, ct);

    [McpServerTool(Name = "card_payment", ReadOnly = false, Destructive = false)]
    [Description("Pays a loan installment with one of the bank's test cards. Ask the customer for the card number and the amount with a form; take the installment number from loans_list. The amount is the customer's choice and may be less than the installment. Leave the installment number out when the amount is more than that installment: the bank then pays the oldest items first and uses the rest for later ones.")]
    public Task<string> CardPaymentAsync(
        [Description("The test card number the customer typed.")] string cardNumber,
        Guid loanId,
        [Description("The amount to pay, such as 1250.00.")] decimal amount,
        [Description("The loan's currency.")] string currency,
        CancellationToken ct,
        [Description("The installment number from the schedule.")] int? scheduleItemNumber = null) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/cards/card-payments",
            new { cardNumber, amount = Money(amount, currency), purpose = "LoanInstallment", loanId, scheduleItemNumber }, ct);

    [McpServerTool(Name = "payoff_request", ReadOnly = false, Destructive = false)]
    [Description("Asks the bank to pay off a loan early from its repayment account, at today's payoff quote. Show the quote (payoff_quote) first.")]
    public Task<string> PayoffAsync(Guid loanId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loans/{loanId}/payoff", null, ct);

    [McpServerTool(Name = "appointment_book", ReadOnly = false, Destructive = false)]
    [Description("Books an in-person meeting about one of the customer's loan applications, in a free slot from appointment_slots.")]
    public Task<string> BookAsync(
        Guid applicationId,
        [Description("The employee id from the slot, such as E002.")] string employeeId,
        [Description("The slot's start time exactly as appointment_slots gave it.")] DateTimeOffset startsAt,
        CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/customers/appointments",
            new { applicationId, customerId = bank.CustomerId, employeeId, startsAt }, ct);

    [McpServerTool(Name = "appointment_cancel", ReadOnly = false, Destructive = true)]
    [Description("Cancels one of the customer's appointments. Use an id from appointments_list.")]
    public Task<string> CancelAppointmentAsync(Guid appointmentId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Delete, $"/api/customers/appointments/{appointmentId}", null, ct);

    [McpServerTool(Name = "loan_application_submit", ReadOnly = false, Destructive = false)]
    [Description("Submits a loan application for the admin to review. Only after loan_simulation showed the request is within the product's limits and the customer agreed. The bank tells the customer the decision; the agent never decides.")]
    public Task<string> SubmitApplicationAsync(
        [Description("The product code from loan_products_list.")] string productCode,
        [Description("The amount asked for, such as 150000.00.")] decimal amount,
        [Description("The product's currency.")] string currency,
        int termMonths,
        [Description("The id of the customer's account that receives the money and pays the installments.")] Guid repaymentAccountId,
        [Description("What the loan is for, in the customer's words.")] string purpose,
        CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/lending/loan-applications",
            new { customerId = bank.CustomerId, productCode, amount = Money(amount, currency), termMonths, repaymentAccountId, purpose, stages = (object?)null }, ct);

    [McpServerTool(Name = DocumentUpload, ReadOnly = false, Destructive = false)]
    [Description("Uploads a document for a loan application. Called by the agent host with the file the person chose; not for the model.")]
    public Task<string> UploadAsync(Guid applicationId, string fileName, string contentType, string contentBase64, CancellationToken ct) =>
        bank.UploadAsync($"/api/customers/customers/{bank.CustomerId}/interactions", applicationId, fileName, contentType,
            Convert.FromBase64String(contentBase64), ct);

    /// <summary>Money as the bank's API takes it: the amount as a string, always with its currency (bank-platform CLAUDE.md).</summary>
    private static object Money(decimal amount, string currency) =>
        new { amount = amount.ToString(CultureInfo.InvariantCulture), currency };
}
