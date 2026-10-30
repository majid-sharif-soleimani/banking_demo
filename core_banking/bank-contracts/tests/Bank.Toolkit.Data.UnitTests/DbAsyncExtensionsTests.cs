using System.Data;
using System.Data.Common;
using Bank.Toolkit.Data;

namespace Bank.Toolkit.Data.UnitTests;

public class DbAsyncExtensionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commit_and_rollback_use_the_providers_async_methods_when_it_has_them()
    {
        var transaction = new AsyncCapableTransaction();

        await ((IDbTransaction)transaction).CommitAsync(Ct);
        await ((IDbTransaction)transaction).RollbackAsync(Ct);

        transaction.Calls.ShouldBe(["CommitAsync", "RollbackAsync"]);
    }

    [Fact]
    public async Task Commit_and_rollback_fall_back_to_the_blocking_methods_for_a_provider_without_async()
    {
        var transaction = new SyncOnlyTransaction();

        await transaction.CommitAsync(Ct);
        await transaction.RollbackAsync(Ct);

        transaction.Calls.ShouldBe(["Commit", "Rollback"]);
    }

    [Fact]
    public async Task Dispose_is_async_when_the_object_supports_it()
    {
        var asyncCapable = new AsyncCapableTransaction();
        var syncOnly = new SyncOnlyTransaction();

        await ((IDisposable)asyncCapable).DisposeAsyncIfPossible();
        await syncOnly.DisposeAsyncIfPossible();

        asyncCapable.Calls.ShouldBe(["DisposeAsync"]);
        syncOnly.Calls.ShouldBe(["Dispose"]);
    }

    /// <summary>Like every real ADO.NET provider: derives from DbTransaction, which has async members.</summary>
    private sealed class AsyncCapableTransaction : DbTransaction
    {
        public List<string> Calls { get; } = [];

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() => Calls.Add("Commit");

        public override void Rollback() => Calls.Add("Rollback");

        public override Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("CommitAsync");
            return Task.CompletedTask;
        }

        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("RollbackAsync");
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            Calls.Add("DisposeAsync");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A provider that implements only the old interface.</summary>
    private sealed class SyncOnlyTransaction : IDbTransaction
    {
        public List<string> Calls { get; } = [];

        public IDbConnection? Connection => null;

        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public void Commit() => Calls.Add("Commit");

        public void Rollback() => Calls.Add("Rollback");

        public void Dispose() => Calls.Add("Dispose");
    }
}
