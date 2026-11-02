using Bank.Contracts.Events;
using Bank.Testing;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Ledger.IntegrationTests.Support;

/// <summary>The real Ledger service with an in-memory broker; SEK and EUR are active, so their charts of accounts exist.</summary>
public sealed class LedgerApp(string connectionString)
    : BankServiceApp<Program>(connectionString, new Dictionary<string, string?> { ["ConnectionStrings:ledger-db"] = connectionString })
{
    public static readonly DateOnly Today = new(2026, 3, 2);

    public static async Task<LedgerApp> StartAsync(PostgresFixture postgres)
    {
        var app = new LedgerApp(await postgres.CreateServiceSchemaAsync("ledger"));
        app.Start();
        await app.OpenBusinessDayAsync(Today);
        foreach (var code in new[] { "SEK", "EUR" })
        {
            await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), Today, "system", code, 2), code);
        }

        return app;
    }
}
