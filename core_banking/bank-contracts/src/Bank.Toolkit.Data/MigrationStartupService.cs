using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bank.Toolkit.Data;

/// <summary>
/// Runs <c>Database.MigrateAsync</c> in <see cref="IHostedLifecycleService.StartingAsync"/>, which the host calls before
/// any hosted service starts, so nothing polls a table that does not exist yet. EF Core takes a database lock while it
/// migrates, so two instances starting together apply each migration once.
/// </summary>
internal sealed class MigrationStartupService<TContext>(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
    where TContext : DbContext
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
