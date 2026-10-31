using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Cards.IntegrationTests.Support;

/// <summary>The real Cards service with an in-memory broker; it already knows SEK, the business date and one disbursed loan.</summary>
public sealed class CardsApp(string connectionString)
    : BankServiceApp<Program>(connectionString, new Dictionary<string, string?> { ["ConnectionStrings:cards-db"] = connectionString })
{
    public static readonly DateOnly Today = new(2026, 2, 20);

    public Guid LoanId { get; } = Guid.CreateVersion7();

    public Guid CustomerId { get; } = Guid.CreateVersion7();

    public static async Task<CardsApp> StartAsync(PostgresFixture postgres)
    {
        var app = new CardsApp(await postgres.CreateServiceSchemaAsync("cards"));
        app.Start();
        await app.OpenBusinessDayAsync(Today);
        await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), Today, "system", "SEK", 2), "SEK");
        await app.DeliverAsync(new LoanDisbursed(Guid.NewGuid(), Today, "system", app.LoanId, Guid.NewGuid(), app.CustomerId, Guid.NewGuid(), new Money(200_000m, "SEK")),
            app.LoanId.ToString());
        return app;
    }
}
