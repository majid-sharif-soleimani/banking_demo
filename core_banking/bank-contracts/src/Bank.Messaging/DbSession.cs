using Npgsql;

namespace Bank.Messaging;

/// <summary>
/// The one connection and transaction of a request or a handled message (scoped). Feature SQL, the outbox
/// publisher and the inbox all use it, which is what makes the state change and its event atomic.
/// Disposing it without <see cref="CommitAsync"/> rolls back.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? connection;
    private NpgsqlTransaction? transaction;

    /// <summary>The open connection; call <see cref="OpenAsync"/> or <see cref="BeginAsync"/> first.</summary>
    public NpgsqlConnection Connection =>
        connection ?? throw new InvalidOperationException("The DbSession is not open. Call OpenAsync or BeginAsync first.");

    /// <summary>The active transaction; pass it to every Dapper call that changes data.</summary>
    public NpgsqlTransaction Transaction =>
        transaction ?? throw new InvalidOperationException("The DbSession has no transaction. Call BeginAsync before changing data or publishing events.");

    public bool InTransaction => transaction is not null;

    /// <summary>Opens the connection without a transaction, for read-only work.</summary>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        connection ??= await dataSource.OpenConnectionAsync(ct);
        return connection;
    }

    public async Task BeginAsync(CancellationToken ct)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The DbSession already has a transaction.");
        }

        var open = await OpenAsync(ct);
        transaction = await open.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        await Transaction.CommitAsync(ct);
        await EndTransactionAsync();
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        await Transaction.RollbackAsync(ct);
        await EndTransactionAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Disposing an uncommitted NpgsqlTransaction rolls it back.
        await EndTransactionAsync();
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }
    }

    private async Task EndTransactionAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }
}
