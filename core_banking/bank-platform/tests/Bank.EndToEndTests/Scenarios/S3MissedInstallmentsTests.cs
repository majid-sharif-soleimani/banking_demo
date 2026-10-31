using Bank.EndToEndTests.Support;

namespace Bank.EndToEndTests.Scenarios;

/// <summary>
/// Scenario 3: an installment is missed. The loan becomes Overdue the day after its due date, a penalty is charged from
/// day 6 (5 grace days), the customer clears the arrears with a manual card payment, and the next end of day classifies
/// the loan as Current again.
/// </summary>
public sealed class S3MissedInstallmentsTests(BankFixture bank) : IClassFixture<BankFixture>
{
    [Fact]
    public async Task A_card_payment_clears_arrears_and_the_loan_returns_to_current()
    {
        var cia = await bank.RegisterCustomerAsync("Cia Holm", "cia.holm@example.com");
        var account = await bank.OpenAccountAsync(cia, "SEK");
        var loanId = await bank.ApproveAndDisburseAsync(await bank.ApplyForLoanAsync(cia, account, "CAR", 30_000m, termMonths: 12));

        // The customer spends the whole loan, so nothing can be collected on the due date.
        await Eventually.Value(() => bank.AccountAsync(account), a => a.Balance.Amount == 30_000m, "the disbursement credited");
        await bank.WithdrawAsync(account, 30_000m);
        var due = (await bank.LoanAsync(loanId)).Schedule[0].DueDate;

        // End of day for due+1 … due+5: overdue, but inside the grace period, so no penalty yet.
        await bank.CloseDaysUntilAsync(due.AddDays(6));
        // Days past due is counted on Lending's own business date, which follows Operations a moment later.
        var loan = await Eventually.Value(() => bank.LoanAsync(loanId), l => l.DaysPastDue == 6, "Lending at due+6");
        loan.Classification.ShouldBe("Overdue");
        loan.Schedule[0].PenaltyDue.Amount.ShouldBe(0m);

        // End of day for due+6: the first penalty day, one day of the product's daily rate on the unpaid principal and
        // interest (penalty is never part of the base), rounded to öre.
        await bank.CloseDayAsync();
        loan = await bank.LoanAsync(loanId);
        var rate = (await bank.Staff.GetAsync<List<ProductView>>("/api/lending/loan-products")).Single(p => p.Code == "CAR").PenaltyDailyRate;
        var item = loan.Schedule[0];
        var basis = item.PrincipalDue.Amount + item.InterestDue.Amount - item.PaidPrincipal.Amount - item.PaidInterest.Amount;
        item.PenaltyDue.Amount.ShouldBe(Math.Round(basis * rate, 2, MidpointRounding.AwayFromZero));
        loan.OverdueAmount.Amount.ShouldBe(loan.Schedule[0].Unpaid);

        // The customer pays the overdue amount, penalty included, with the test card that always approves.
        var payment = await bank.Customer(cia.Id).PostAsync<CardPaymentView>("/api/cards/card-payments", new
        {
            cardNumber = "9999 0000 0000 0001", amount = loan.OverdueAmount, purpose = "LoanInstallment", loanId, scheduleItemNumber = (int?)null,
        }, idempotent: true);
        payment.Status.ShouldBe("Captured");
        loan = await Eventually.Value(() => bank.LoanAsync(loanId), l => l.Schedule[0].Status == "Paid", "the card payment allocated");
        loan.OverdueAmount.Amount.ShouldBe(0m);
        loan.Classification.ShouldBe("Overdue"); // unchanged until end of day

        // The classification is recomputed at end of day.
        await bank.CloseDayAsync();
        (await bank.LoanAsync(loanId)).Classification.ShouldBe("Current");
    }
}
