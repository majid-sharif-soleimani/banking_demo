using Microsoft.Extensions.Hosting;

namespace Bank.Toolkit.Data.Migrations;

/// <summary>
/// Runs the scripts of every registered <see cref="MigrationSource"/> in <see cref="IHostedLifecycleService.StartingAsync"/>,
/// which the host calls before any hosted service starts, so nothing polls a table that does not exist yet.
/// </summary>
internal sealed class MigrationStartupService(SqlMigrationRunner runner, IEnumerable<MigrationSource> sources) : IHostedLifecycleService
{
    // A source registered twice must not run its scripts twice.
    private readonly IReadOnlyList<MigrationScript> scripts = sources.SelectMany(s => s.Scripts).DistinctBy(s => s.Id).ToList();

    public Task StartingAsync(CancellationToken cancellationToken) => runner.RunAsync(scripts, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
