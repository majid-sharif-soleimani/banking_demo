using Bank.Operations.Api.Data;
using Bank.Testing;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Operations.IntegrationTests.Support;

/// <summary>A fresh "operations" role and schema with the service's migrations applied, as the service would see it.</summary>
public sealed class OperationsDatabase
{
    private OperationsDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<OperationsDatabase> CreateAsync(PostgresFixture postgres)
    {
        var database = new OperationsDatabase(await postgres.CreateServiceSchemaAsync("operations"));
        await using var context = database.NewContext();
        await context.Database.MigrateAsync(Ct);
        return database;
    }

    /// <summary>A new context per unit of work, like one request or one handled message.</summary>
    public OperationsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>();
        options.UseNpgsqlWithBankConventions(ConnectionString);
        return new OperationsDbContext(options.Options);
    }

    /// <summary>Reads with plain SQL, outside EF, so the test sees what is really committed.</summary>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
