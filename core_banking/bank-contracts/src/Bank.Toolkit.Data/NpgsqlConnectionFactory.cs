using System.Data;
using Npgsql;

namespace Bank.Toolkit.Data;

/// <summary>
/// The PostgreSQL connection factory: the only production type in the bank-contracts packages that names Npgsql
/// (bank-contracts ADR 0010). A service's connection string carries its own role, and its schema as the search path.
/// </summary>
public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory, IAsyncDisposable
{
    // One data source per service: it owns the connection pool.
    private readonly NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

    public async Task<IDbConnection> OpenAsync(CancellationToken ct) => await dataSource.OpenConnectionAsync(ct);

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
