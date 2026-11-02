using Aspire.Hosting;
using Npgsql;

namespace Bank.Platform.IntegrationTests;

/// <summary>A service started by the AppHost applied its migrations in its own schema, with the role the AppHost gave it.</summary>
public sealed class ServiceStartupTests(PlatformFixture platform)
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Operations_migrates_its_schema_on_startup()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(HealthTimeout);
        // Migrations run before the host starts listening, so a healthy service has migrated.
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync("operations", timeout.Token);

        await using var connection = new NpgsqlConnection(await platform.ConnectionStringAsync("operations-db"));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(
            "SELECT current_schema() || ':' || count(*) FROM currency", connection);

        (await command.ExecuteScalarAsync(timeout.Token)).ShouldBe("operations:3");
    }
}
