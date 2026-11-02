using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class LoanDisbursedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_disbursed_loan_credits_the_repayment_account_once_without_a_deposit_event()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        var loanId = Guid.NewGuid();
        var disbursed = new LoanDisbursed(Guid.NewGuid(), AccountsApp.Today, "E001", loanId, Guid.NewGuid(), app.CustomerId, account.Id, new Money(200_000m, "SEK"));

        await app.DeliverAsync(disbursed, loanId.ToString());
        await app.DeliverAsync(disbursed with { EventId = Guid.NewGuid() }, loanId.ToString()); // the same fact again

        (await app.BalanceAsync(account.Id)).ShouldBe(200_000m);
        var statement = await app.GetJsonAsync<StatementResponse>($"/accounts/{account.Id}/statement?from={AccountsApp.Today:yyyy-MM-dd}&to={AccountsApp.Today:yyyy-MM-dd}");
        statement.Lines.ShouldHaveSingleItem().ShouldSatisfyAllConditions(l => l.Kind.ShouldBe("LoanDisbursement"), l => l.Reference.ShouldBe(loanId));
        await app.OutboxDrainedAsync();
        app.Published<MoneyDeposited>().ShouldBeEmpty();
    }
}
