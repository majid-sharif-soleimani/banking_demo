using System.Data;
using System.Data.Common;

namespace Bank.Toolkit.Data;

/// <summary>
/// Async versions of the blocking members of <see cref="IDbConnection"/> and <see cref="IDbTransaction"/> (bank-contracts ADR 0010).
/// IDbConnection predates async and has only Open, BeginTransaction and Commit. Every ADO.NET provider's connection
/// also derives from <see cref="DbConnection"/>, which has real async versions, so these call them when they exist
/// and fall back to the blocking call only for a provider that has none. Dapper's async methods do the same.
/// They never wrap a blocking call in Task.Run: that would only move the blocked thread elsewhere.
/// </summary>
public static class DbAsyncExtensions
{
    public static async Task<IDbTransaction> BeginTransactionAsync(this IDbConnection connection, CancellationToken ct) =>
        connection is DbConnection db ? await db.BeginTransactionAsync(ct) : connection.BeginTransaction();

    public static Task CommitAsync(this IDbTransaction transaction, CancellationToken ct)
    {
        if (transaction is DbTransaction db)
        {
            return db.CommitAsync(ct);
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public static Task RollbackAsync(this IDbTransaction transaction, CancellationToken ct)
    {
        if (transaction is DbTransaction db)
        {
            return db.RollbackAsync(ct);
        }

        transaction.Rollback();
        return Task.CompletedTask;
    }

    /// <summary>Disposes asynchronously when the object supports it; an uncommitted transaction rolls back here.</summary>
    public static ValueTask DisposeAsyncIfPossible(this IDisposable disposable)
    {
        if (disposable is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        disposable.Dispose();
        return ValueTask.CompletedTask;
    }
}
