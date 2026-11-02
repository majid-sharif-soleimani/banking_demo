using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;

namespace Bank.Lending.IntegrationTests.Support;

/// <summary>
/// Stands in for Accounts: answers every InstallmentDue and LoanPayoffRequested Lending sends, in full from
/// <see cref="Balance"/> or not at all, through Lending's inbox, as the real Accounts would through Kafka.
/// </summary>
public sealed class FakeAccounts
{
    private readonly LendingApp app;

    public FakeAccounts(LendingApp app, decimal balance)
    {
        this.app = app;
        Balance = balance;
        app.Broker.Subscribe(async (message, ct) =>
        {
            if (message.SchemaUri == InstallmentDue.SchemaUri)
            {
                var due = JsonSerializer.Deserialize<InstallmentDue>(message.Payload, ContractJson.Options)!;
                await AnswerAsync(due.LoanId, due.CollectionId, due.ScheduleItemNumber, "Installment", due.Amount, ct);
            }
            else if (message.SchemaUri == LoanPayoffRequested.SchemaUri)
            {
                var payoff = JsonSerializer.Deserialize<LoanPayoffRequested>(message.Payload, ContractJson.Options)!;
                await AnswerAsync(payoff.LoanId, payoff.CollectionId, null, "Payoff", payoff.Amount, ct);
            }
        });
    }

    public decimal Balance { get; set; }

    public List<Money> Debits { get; } = [];

    private async Task AnswerAsync(Guid loanId, Guid collectionId, int? item, string purpose, Money amount, CancellationToken ct)
    {
        var date = app.BusinessDate;
        if (Balance >= amount.Amount)
        {
            Balance -= amount.Amount;
            Debits.Add(amount);
            await app.DeliverAsync(new PaymentCollected(Guid.NewGuid(), date, "system", loanId, collectionId, item, purpose, amount), loanId.ToString(), ct);
        }
        else
        {
            await app.DeliverAsync(new PaymentFailed(Guid.NewGuid(), date, "system", loanId, collectionId, app.CustomerId, item, purpose, amount, "insufficient_funds"),
                loanId.ToString(), ct);
        }
    }
}
