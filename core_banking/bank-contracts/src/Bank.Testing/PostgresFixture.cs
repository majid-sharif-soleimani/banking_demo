using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Bank.Testing;

/// <summary>
/// One PostgreSQL container for a test collection. Each test asks for its own service schemas, so tests never see
/// each other's rows. Credentials are generated at runtime and exist only for the life of the container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Every service host in a test keeps its own connection pool, and test classes run in parallel: the default of 100
    // connections runs out (53300) long before the tests do.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.3")
        .WithCommand("-c", "max_connections=500")
        .Build();

    public string AdminConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>
    /// Creates a login role and a schema it owns, named like a service, and returns a connection string for that
    /// role with the schema as its search path, the same shape as the platform's one role and schema per service.
    /// </summary>
    public async Task<string> CreateServiceSchemaAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 63)].ToLowerInvariant();
        var password = Convert.ToHexString(Guid.NewGuid().ToByteArray());

        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            // Identifiers cannot be parameters; the name is generated above from a prefix the test chose.
            command.CommandText = $"""
                CREATE ROLE "{name}" LOGIN PASSWORD '{password}';
                CREATE SCHEMA "{name}" AUTHORIZATION "{name}";
                """;
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = name,
            Password = password,
            SearchPath = name,
        }.ConnectionString;
    }
}
