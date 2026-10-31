using Bank.EndToEndTests.Support;

namespace Bank.EndToEndTests.Scenarios;

/// <summary>
/// Scenario 4: nothing is paid. After 90 days past due the loan is in Default (DefaultAfterDays of the product), and the
/// customer has been emailed on the way: every failed collection, and each worse classification.
/// </summary>
public sealed class S4DefaultTests(BankFixture bank) : IClassFixture<BankFixture>
{
    [Fact]
    public async Task Ninety_one_days_unpaid_is_default_with_emails()
    {
        var dan = await bank.RegisterCustomerAsync("Dan Berg", "dan.berg@example.com");
        var account = await bank.OpenAccountAsync(dan, "SEK");
        var loanId = await bank.ApproveAndDisburseAsync(await bank.ApplyForLoanAsync(dan, account, "CAR", 30_000m, termMonths: 12));
        await Eventually.Value(() => bank.AccountAsync(account), a => a.Balance.Amount == 30_000m, "the disbursement credited");
        await bank.WithdrawAsync(account, 30_000m);
        var due = (await bank.LoanAsync(loanId)).Schedule[0].DueDate;

        // End of day up to due+89: still Overdue.
        await bank.CloseDaysUntilAsync(due.AddDays(90));
        (await bank.LoanAsync(loanId)).Classification.ShouldBe("Overdue");

        // End of day for due+90 and due+91: 91 days unpaid.
        await bank.CloseDaysUntilAsync(due.AddDays(92));
        var loan = await Eventually.Value(() => bank.LoanAsync(loanId), l => l.DaysPastDue == 92, "Lending at due+92");
        loan.Classification.ShouldBe("Default");

        // One warning per worse classification (Overdue, then Default), and the failed collections along the way.
        var subjects = await Eventually.Value(() => bank.EmailSubjectsAsync(dan.Email),
            s => s.Count(x => x == "Your loan payments are late") >= 2, "the Overdue and Default emails");
        subjects.Count(x => x == "Your loan payments are late").ShouldBe(2);
        subjects.ShouldContain("A loan payment could not be collected");
    }
}
