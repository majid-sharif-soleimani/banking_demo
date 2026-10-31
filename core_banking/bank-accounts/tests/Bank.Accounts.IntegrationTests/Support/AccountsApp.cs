using System.Net.Http.Json;
using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.Api.Features.Movements;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Accounts.IntegrationTests.Support;

/// <summary>
/// The real Accounts service on a fresh schema with an in-memory broker. <see cref="StartAsync"/> also delivers what
/// Accounts would have received from Kafka: the business date, SEK/EUR/USD activated, and one registered customer.
/// </summary>
public sealed class AccountsApp(string connectionString, Action<IServiceCollection>? configure)
    : BankServiceApp<Program>(connectionString, new Dictionary<string, string?> { ["ConnectionStrings:accounts-db"] = connectionString }, configure)
{
    public static readonly DateOnly Today = new(2026, 3, 1);

    public Guid CustomerId { get; } = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<AccountsApp> StartAsync(PostgresFixture postgres, Action<IServiceCollection>? configure = null)
    {
        var app = new AccountsApp(await postgres.CreateServiceSchemaAsync("accounts"), configure);
        app.Start();
        await app.OpenBusinessDayAsync(Today);
        foreach (var code in new[] { "SEK", "EUR", "USD" })
        {
            await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), Today, "system", code, 2), code);
        }

        await app.RegisterCustomerAsync(app.CustomerId);
        return app;
    }

    public Task RegisterCustomerAsync(Guid customerId) =>
        DeliverAsync(new CustomerRegistered(Guid.NewGuid(), Today, "system", customerId, "Ada Lovelace", "en"), customerId.ToString());

    public async Task<AccountResponse> OpenAccountAsync(string currency = "SEK", Guid? customerId = null)
    {
        var response = await PostJsonAsync("/accounts", new OpenAccount.Request(customerId ?? CustomerId, "Current", currency));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
    }

    public Task<HttpResponseMessage> DepositAsync(Guid accountId, decimal amount, string currency = "SEK", string? key = null) =>
        PostJsonAsync($"/accounts/{accountId}/deposits", new CashMovements.Request(new Money(amount, currency), null), key ?? Guid.NewGuid().ToString());

    public Task<HttpResponseMessage> WithdrawAsync(Guid accountId, decimal amount, string currency = "SEK", string? key = null) =>
        PostJsonAsync($"/accounts/{accountId}/withdrawals", new CashMovements.Request(new Money(amount, currency), null), key ?? Guid.NewGuid().ToString());

    public async Task<decimal> BalanceAsync(Guid accountId) => (await GetJsonAsync<AccountResponse>($"/accounts/{accountId}")).Balance.Amount;
}

/// <summary>Returns the given numbers in order, to force a collision.</summary>
public sealed class ScriptedAccountNumbers(params string[] numbers) : IAccountNumberGenerator
{
    private int next = -1;

    public Task<string> NextAsync(CancellationToken ct) => Task.FromResult(numbers[Math.Min(Interlocked.Increment(ref next), numbers.Length - 1)]);
}
