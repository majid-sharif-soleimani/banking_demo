using Microsoft.Extensions.Hosting;

namespace Bank.Messaging.Migrations;

/// <summary>
/// Runs the migrations in <see cref="IHostedLifecycleService.StartingAsync"/>, which the host calls before any hosted
/// service starts, so the outbox dispatcher never polls a table that does not exist yet.
/// </summary>
internal sealed class MigrationStartupService(SqlMigrationRunner runner, IReadOnlyList<MigrationScript> scripts) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => runner.RunAsync(scripts, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
