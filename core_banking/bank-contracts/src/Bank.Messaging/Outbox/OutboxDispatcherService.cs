using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bank.Messaging.Outbox;

/// <summary>Polls the outbox for as long as the service runs: again at once after a full batch, otherwise after PollingInterval.</summary>
internal sealed class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    IOptions<BankMessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                sent = await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A database outage must not stop the dispatcher for good; the rows stay in the outbox.
                logger.LogError(ex, "Outbox poll failed; retrying after {Interval}.", options.Value.PollingInterval);
            }

            if (sent < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.PollingInterval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
