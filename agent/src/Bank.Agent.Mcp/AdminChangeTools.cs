using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Bank.Agent.Mcp;

/// <summary>
/// The admin's change tools. As for the customer, each is ReadOnly = false, so the agent host shows a confirmation and
/// runs it only after the admin's own click (decision A11). Approving or rejecting a loan is always the admin's click,
/// never the model's decision (business question 1).
/// </summary>
[McpServerToolType]
public sealed class AdminChangeTools(BankApi bank)
{
    [McpServerTool(Name = "currency_activate", ReadOnly = false, Destructive = false)]
    [Description("Activates a currency, so accounts and loans can use it. Cannot be undone.")]
    public Task<string> ActivateCurrencyAsync([Description("Currency code, such as EUR.")] string code, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/operations/currencies/{Uri.EscapeDataString(code)}/activate", null, ct);

    [McpServerTool(Name = "business_day_close", ReadOnly = false, Destructive = false)]
    [Description("Closes the current business day. The bank then runs end of day (interest, penalties, standing orders, loan classification) and opens the next day. Check the trial balance first.")]
    public Task<string> CloseBusinessDayAsync(CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, "/api/operations/business-days/current/close", null, ct);

    [McpServerTool(Name = "loan_application_start_review", ReadOnly = false, Destructive = false)]
    [Description("Moves a Submitted application (or one whose appointment is over) to UnderReview.")]
    public Task<string> StartReviewAsync(Guid applicationId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/review", null, ct);

    [McpServerTool(Name = "loan_application_approve", ReadOnly = false, Destructive = false)]
    [Description("Approves an application that is UnderReview. Only when the admin has said they approve it.")]
    public Task<string> ApproveAsync(Guid applicationId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/approve", null, ct);

    [McpServerTool(Name = "loan_application_reject", ReadOnly = false, Destructive = false)]
    [Description("Rejects an open application with a reason the customer will read in their email. Only when the admin has said they reject it.")]
    public Task<string> RejectAsync(Guid applicationId, [Description("The reason, in the admin's words.")] string reason, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/reject", new { reason }, ct);

    [McpServerTool(Name = "loan_application_disburse", ReadOnly = false, Destructive = false)]
    [Description("Pays out an Approved application: the loan is created and the money goes to the customer's account.")]
    public Task<string> DisburseAsync(Guid applicationId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/disburse", null, ct);

    [McpServerTool(Name = "loan_application_request_information", ReadOnly = false, Destructive = false)]
    [Description("Asks the customer for documents for an application that is UnderReview. The bank emails the customer the list.")]
    public Task<string> RequestInformationAsync(
        Guid applicationId,
        [Description("What the customer must send, one item each, such as 'Payslip for October'.")] string[] items,
        CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/information-requests", new { items }, ct);

    [McpServerTool(Name = "information_request_complete", ReadOnly = false, Destructive = false)]
    [Description("Marks an open information request as completed, after the admin has checked the documents. The application goes back to UnderReview.")]
    public Task<string> CompleteInformationRequestAsync(Guid informationRequestId, CancellationToken ct) =>
        bank.ChangeAsync(HttpMethod.Post, $"/api/customers/information-requests/{informationRequestId}/complete", null, ct);
}
