using System.Net;
using System.Net.Http.Headers;
using Bank.EndToEndTests.Support;

namespace Bank.EndToEndTests.Scenarios;

/// <summary>
/// Scenario 2: an annuity loan from application to early payoff. The review asks for information, the customer uploads a
/// document, the loan is approved and disbursed, three installments are collected at end of day, and the customer pays
/// off the rest early. Afterwards nothing is owed: the loan is closed in Lending and loans receivable is zero in the ledger.
/// </summary>
public sealed class S2AnnuityLoanLifecycleTests(BankFixture bank) : IClassFixture<BankFixture>
{
    [Fact]
    public async Task An_annuity_loan_runs_from_application_to_early_payoff()
    {
        var bo = await bank.RegisterCustomerAsync("Bo Ek", "bo.ek@example.com");
        var account = await bank.OpenAccountAsync(bo, "SEK");
        await bank.DepositAsync(account, 20_000m);

        // Apply, review, and ask for a document.
        var application = await bank.ApplyForLoanAsync(bo, account, "CAR", 60_000m, termMonths: 12);
        await bank.Staff.PostAsync<ApplicationView>($"/api/lending/loan-applications/{application.Id}/review");
        (await bank.Staff.PostAsync<ApplicationView>($"/api/lending/loan-applications/{application.Id}/information-requests",
            new { items = new[] { "Proof of income" } })).Status.ShouldBe("InfoRequested");

        // Customers opens an information request (from InformationRequested) and emails the customer.
        var request = await Eventually.Value(
            () => bank.Customer(bo.Id).GetAsync<List<InformationRequestView>>($"/api/customers/information-requests?applicationId={application.Id}"),
            r => r.Count == 1, "the information request in Customers");
        (await Eventually.Value(() => bank.EmailSubjectsAsync(bo.Email), s => s.Count >= 1, "the information request email"))
            .ShouldContain("We need more information for your loan application");

        // The customer uploads the document and the request is completed; Lending moves back to UnderReview.
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Upload"), "kind" },
            { new StringContent(application.Id.ToString()), "applicationId" },
        };
        var file = new ByteArrayContent("%PDF-1.4 salary statement"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "salary.pdf");
        var upload = await bank.Customer(bo.Id).SendAsync(HttpMethod.Post, $"/api/customers/customers/{bo.Id}/interactions", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await bank.Customer(bo.Id).PostAsync<InformationRequestView>($"/api/customers/information-requests/{request.Single().Id}/complete");
        await Eventually.Value(
            () => bank.Staff.GetAsync<ApplicationView>($"/api/lending/loan-applications/{application.Id}"),
            a => a.Status == "UnderReview", "the application back under review");

        // Approve and disburse: the principal lands on the repayment account.
        var loanId = await bank.ApproveAndDisburseAsync(application, review: false);
        await Eventually.Value(() => bank.AccountAsync(account), a => a.Balance.Amount == 80_000m, "the disbursement credited");

        // Three months of end of day: each installment is collected on its due date.
        var loan = await bank.LoanAsync(loanId);
        var thirdDue = loan.Schedule[2].DueDate;
        await bank.CloseDaysUntilAsync(thirdDue.AddDays(1));
        loan = await Eventually.Value(() => bank.LoanAsync(loanId), l => l.Schedule.Take(3).All(i => i.Status == "Paid"), "three installments paid");
        loan.Schedule.Skip(3).ShouldAllBe(i => i.Status != "Paid");
        loan.Classification.ShouldBe("Current");

        // Early payoff: quoted for today, debited by Accounts, settled by Lending.
        var quote = await bank.Staff.GetAsync<PayoffQuoteView>($"/api/lending/loans/{loanId}/payoff-quote");
        quote.WaivedInterest.Amount.ShouldBeGreaterThan(0m);
        var balanceBefore = (await bank.AccountAsync(account)).Balance.Amount;
        var payoff = await bank.Customer(bo.Id).SendAsync(HttpMethod.Post, $"/api/lending/loans/{loanId}/payoff");
        payoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await payoff.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        loan = await Eventually.Value(() => bank.LoanAsync(loanId), l => l.Status == "PaidOffEarly", "the loan paid off early");
        loan.OutstandingPrincipal.Amount.ShouldBe(0m);
        (await Eventually.Value(() => bank.AccountAsync(account), a => a.Balance.Amount < balanceBefore, "the payoff debited"))
            .Balance.Amount.ShouldBe(balanceBefore - quote.Total.Amount);

        // The books: balanced, nothing left in loans receivable, deposits equal to the account.
        var books = await Eventually.Value(() => bank.TrialBalanceAsync("SEK"), b => b.BalanceOf(Gl.LoansReceivable) == 0m, "loans receivable at zero");
        books.TotalDebit.ShouldBe(books.TotalCredit);
        books.BalanceOf(Gl.CustomerDeposits).ShouldBe((await bank.AccountAsync(account)).Balance.Amount);
    }
}
