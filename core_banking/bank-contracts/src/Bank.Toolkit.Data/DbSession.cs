using System.Data;

namespace Bank.Toolkit.Data;

/// <summary>
/// The one connection and transaction of a unit of work (bank-contracts ADR 0009): an HTTP request or a handled message (scoped),
/// and also one dispatcher poll or one migration run. Everything in one unit of work uses it, which is what makes
/// several statements atomic (in Bank.Messaging: the state change, its outbox row and the inbox row). It sees only <see cref="IDbConnection"/>; the
/// provider is known only to the <see cref="IDbConnectionFactory"/> (bank-contracts ADR 0010).
/// Disposing it without <see cref="CommitAsync"/> rolls back.
/// </summary>
public sealed class DbSession(IDbConnectionFactory connectionFactory) : IAsyncDisposable
{
    private IDbConnection? connection;
    private IDbTransaction? transaction;

    /// <summary>The open connection; call <see cref="OpenAsync"/> or <see cref="BeginAsync"/> first.</summary>
    public IDbConnection Connection =>
        connection ?? throw new InvalidOperationException("The DbSession is not open. Call OpenAsync or BeginAsync first.");

    /// <summary>The active transaction; pass it to every Dapper call that changes data.</summary>
    public IDbTransaction Transaction =>
        transaction ?? throw new InvalidOperationException("The DbSession has no transaction. Call BeginAsync before changing data or publishing events.");

    public bool InTransaction => transaction is not null;

    /// <summary>Opens the connection without a transaction, for read-only work.</summary>
    public async Task<IDbConnection> OpenAsync(CancellationToken ct)
    {
        connection ??= await connectionFactory.OpenAsync(ct);
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
        // Disposing an uncommitted transaction rolls it back.
        await EndTransactionAsync();
        if (connection is not null)
        {
            await connection.DisposeAsyncIfPossible();
            connection = null;
        }
    }

    private async Task EndTransactionAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsyncIfPossible();
            transaction = null;
        }
    }
}
