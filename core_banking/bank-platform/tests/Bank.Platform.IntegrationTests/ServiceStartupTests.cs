using Aspire.Hosting;
using Npgsql;

namespace Bank.Platform.IntegrationTests;

/// <summary>A service started by the AppHost applied its migrations in its own schema, with the role the AppHost gave it.</summary>
public sealed class ServiceStartupTests(PlatformFixture platform)
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(3);

    [Theory]
    [InlineData("operations", "currency", "operations:3")]
    [InlineData("customers", "customer", "customers:0")]
    [InlineData("accounts", "account", "accounts:0")]
    [InlineData("lending", "loan_product", "lending:3")]
    [InlineData("cards", "test_card", "cards:3")]
    [InlineData("ledger", "gl_account", "ledger:0")]
    public async Task A_service_migrates_its_own_schema_on_startup(string service, string table, string expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(HealthTimeout);
        // Migrations run before the host starts listening, so a healthy service has migrated.
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync(service, timeout.Token);

        await using var connection = new NpgsqlConnection(await platform.ConnectionStringAsync($"{service}-db"));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(
            $"SELECT current_schema() || ':' || count(*) FROM {table}", connection);

        (await command.ExecuteScalarAsync(timeout.Token)).ShouldBe(expected);
    }
}
