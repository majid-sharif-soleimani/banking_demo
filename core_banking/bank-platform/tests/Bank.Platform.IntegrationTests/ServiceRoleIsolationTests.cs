using Aspire.Hosting;
using Npgsql;

namespace Bank.Platform.IntegrationTests;

/// <summary>
/// The data boundary between services is enforced by PostgreSQL permissions, not by convention (design plan ADR 11,
/// bank-platform ADR 0001). Each test connects as one service role, exactly as that service will.
/// </summary>
public sealed class ServiceRoleIsolationTests(PlatformFixture platform)
{
    // Must match BankDatabase.Services in the AppHost and the files in db/init (the agent host's role is in 002).
    public static readonly TheoryData<string> Services = new("operations", "customers", "accounts", "cards", "lending", "ledger", "agent");

    private const string InsufficientPrivilege = "42501";

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Role_can_create_and_read_tables_in_its_own_schema(string service)
    {
        await using var connection = await OpenAsAsync(service);

        // No schema named: the role's search path must point to its own schema.
        await ExecuteAsync(connection, "CREATE TABLE own_probe (id int); INSERT INTO own_probe VALUES (1);");

        (await ScalarAsync(connection, "SELECT count(*) FROM own_probe")).ShouldBe(1L);
        (await ScalarAsync(connection, "SELECT current_schema()")).ShouldBe(service);
        (await ScalarAsync(connection, "SELECT table_schema FROM information_schema.tables WHERE table_name = 'own_probe'"))
            .ShouldBe(service);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Role_cannot_read_another_schema(string service)
    {
        await PrepareProbeTablesAsync();
        await using var connection = await OpenAsAsync(service);

        foreach (var other in OtherServices(service))
        {
            var error = await Should.ThrowAsync<PostgresException>(
                () => ScalarAsync(connection, $"SELECT count(*) FROM {other}.isolation_probe"));

            error.SqlState.ShouldBe(InsufficientPrivilege, $"{service} must not read {other}.isolation_probe");
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Role_cannot_write_to_another_schema(string service)
    {
        await PrepareProbeTablesAsync();
        await using var connection = await OpenAsAsync(service);

        foreach (var other in OtherServices(service))
        {
            var error = await Should.ThrowAsync<PostgresException>(
                () => ExecuteAsync(connection, $"CREATE TABLE {other}.intruder (id int)"));

            error.SqlState.ShouldBe(InsufficientPrivilege, $"{service} must not create tables in {other}");
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Role_cannot_create_tables_in_public_or_new_schemas(string service)
    {
        await using var connection = await OpenAsAsync(service);

        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "CREATE TABLE public.intruder (id int)")))
            .SqlState.ShouldBe(InsufficientPrivilege);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, $"CREATE SCHEMA {service}_extra")))
            .SqlState.ShouldBe(InsufficientPrivilege);
    }

    private static IEnumerable<string> OtherServices(string service) =>
        Services.Select(row => row.Data).Where(other => other != service);

    /// <summary>
    /// Gives every schema one table, created by its owner, so a failed read means "no permission" and never
    /// "table does not exist". Safe to run from several tests: IF NOT EXISTS.
    /// </summary>
    private async Task PrepareProbeTablesAsync()
    {
        foreach (var owner in Services.Select(row => row.Data))
        {
            await using var connection = await OpenAsAsync(owner);
            await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS isolation_probe (id int)");
        }
    }

    private async Task<NpgsqlConnection> OpenAsAsync(string service)
    {
        var ct = TestContext.Current.CancellationToken;
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync("bank", ct);

        var connection = new NpgsqlConnection(await platform.ConnectionStringAsync($"{service}-db"));
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
