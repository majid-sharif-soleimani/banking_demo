using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.Options;

namespace Bank.Agent.Host.Features.Auth;

/// <summary>
/// Keeps chats honest when browsers go away (phase 2 design plan, decision A12). At startup it closes every chat left
/// open by the previous process, whose sessions died with it. Then, every Agent:SweepInterval, it removes host sessions
/// idle for Agent:IdleTimeout and closes their chats with session_expired, so a closed tab does not leave an open chat.
/// </summary>
public sealed class SessionSweeper(
    IServiceScopeFactory scopes, HostSessions sessions, TimeProvider time, IOptions<AgentOptions> options, ILogger<SessionSweeper> log)
    : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Migrations ran in StartingAsync (Bank.Toolkit.Data), before any StartAsync.
        await using var scope = scopes.CreateAsyncScope();
        var closed = await scope.ServiceProvider.GetRequiredService<ChatStore>().CloseAllOpenAsync(cancellationToken);
        if (closed > 0)
        {
            log.LogInformation("Closed {Count} chats left open by the previous start", closed);
        }

        await base.StartAsync(cancellationToken);
    }

    /// <summary>One sweep; the timer calls it, and tests call it after moving the clock.</summary>
    public async Task SweepOnceAsync(CancellationToken ct)
    {
        var idle = sessions.FindIdle();
        if (idle.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var chats = scope.ServiceProvider.GetRequiredService<ChatStore>();
        foreach (var session in idle)
        {
            // Close first, forget after: if closing fails, the session is still idle on the next sweep and tried again.
            await chats.CloseAsync(session.ChatId, ChatCloseReason.SessionExpired, ct);
            sessions.End(session.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed sweep is retried on the next tick: sessions are forgotten only after their chat is closed.
                log.LogWarning("Session sweep failed: {Error}", ex.GetType().Name);
            }
        }
    }
}
