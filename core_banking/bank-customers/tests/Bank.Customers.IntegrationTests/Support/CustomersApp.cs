using Bank.Common;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Customers.IntegrationTests.Support;

/// <summary>The real Customers service on a fresh schema, with an in-memory broker and an in-memory email sender.</summary>
public sealed class CustomersApp(string connectionString)
    : BankServiceApp<Program>(connectionString, Settings(connectionString), services =>
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender, InMemoryEmailSender>();
    })
{
    public static readonly DateOnly Today = new(2026, 3, 1);

    public InMemoryEmailSender Emails => (InMemoryEmailSender)Services.GetRequiredService<IEmailSender>();

    /// <summary>Starts the service with business date <see cref="Today"/>.</summary>
    public static async Task<CustomersApp> StartAsync(PostgresFixture postgres)
    {
        var app = new CustomersApp(await postgres.CreateServiceSchemaAsync("customers"));
        app.Start();
        await app.OpenBusinessDayAsync(Today);
        return app;
    }

    private static Dictionary<string, string?> Settings(string connectionString) => new()
    {
        ["ConnectionStrings:customers-db"] = connectionString,
        ["ConnectionStrings:mailpit"] = "Endpoint=smtp://localhost:1",
    };
}
