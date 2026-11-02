using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.Inbox;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class LoanCollectionTests(PostgresFixture postgres)
{
    private static InstallmentDue Due(Guid account, decimal amount, Guid? collectionId = null) =>
        new(Guid.NewGuid(), AccountsApp.Today, "system", Guid.NewGuid(), collectionId ?? Guid.NewGuid(), 1, account, new Money(amount, "SEK"), AccountsApp.Today);

    [Fact]
    public async Task A_covered_installment_is_debited_in_full_and_PaymentCollected_is_published()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 10_000m);
        var due = Due(account.Id, 6_170.85m);

        await app.DeliverAsync(due, due.LoanId.ToString());

        (await app.BalanceAsync(account.Id)).ShouldBe(3_829.15m);
        await Eventually.AssertAsync(() =>
        {
            var collected = app.Published<PaymentCollected>().ShouldHaveSingleItem();
            collected.ShouldSatisfyAllConditions(
                e => e.CollectionId.ShouldBe(due.CollectionId), e => e.Purpose.ShouldBe("Installment"),
                e => e.ScheduleItemNumber.ShouldBe(1), e => e.Amount.ShouldBe(new Money(6_170.85m, "SEK")));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_uncovered_installment_is_not_debited_at_all_and_PaymentFailed_is_published()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 6_000m);
        var due = Due(account.Id, 6_170.85m);

        await app.DeliverAsync(due, due.LoanId.ToString());

        (await app.BalanceAsync(account.Id)).ShouldBe(6_000m); // no partial debit
        await Eventually.AssertAsync(() =>
        {
            var failed = app.Published<PaymentFailed>().ShouldHaveSingleItem();
            failed.CustomerId.ShouldBe(app.CustomerId);
            failed.Reason.ShouldBe("insufficient_funds");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Duplicate_InstallmentDue_debits_once()
    {
        // The same message twice (inbox) and the same collection id under a new message id (loan_collection key, ADR 0005).
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 20_000m);
        var due = Due(account.Id, 6_170.85m);

        (await app.DeliverAsync(due, "k")).ShouldBe(ProcessOutcome.Handled);
        (await app.DeliverAsync(due, "k")).ShouldBe(ProcessOutcome.Duplicate);
        await app.DeliverAsync(due with { EventId = Guid.NewGuid() }, "k");

        (await app.BalanceAsync(account.Id)).ShouldBe(20_000m - 6_170.85m);
        (await app.ScalarAsync<long>("SELECT count(*) FROM account_entry WHERE kind = 'LoanRepayment'")).ShouldBe(1);
        await app.OutboxDrainedAsync();
        app.Published<PaymentCollected>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_payoff_is_collected_or_fails_as_a_whole()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 1_000m);
        var loanId = Guid.NewGuid();

        await app.DeliverAsync(new LoanPayoffRequested(Guid.NewGuid(), AccountsApp.Today, "system", loanId, Guid.NewGuid(), account.Id, new Money(1_500m, "SEK")), "k");
        await app.DeliverAsync(new LoanPayoffRequested(Guid.NewGuid(), AccountsApp.Today, "system", loanId, Guid.NewGuid(), account.Id, new Money(900m, "SEK")), "k");

        (await app.BalanceAsync(account.Id)).ShouldBe(100m);
        await Eventually.AssertAsync(() =>
        {
            app.Published<PaymentFailed>().ShouldHaveSingleItem().Purpose.ShouldBe("Payoff");
            app.Published<PaymentCollected>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(e => e.Purpose.ShouldBe("Payoff"), e => e.ScheduleItemNumber.ShouldBeNull());
            return Task.CompletedTask;
        });
    }
}
