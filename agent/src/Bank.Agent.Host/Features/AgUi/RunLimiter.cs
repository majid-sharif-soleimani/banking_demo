using System.Threading.RateLimiting;
using Bank.Agent.Host.Features.Agent;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>
/// At most Agent:Model:RunsPerMinute runs per user and minute (agent ADR 0007). Every run costs model tokens, and the test
/// logins are public (workspace ADR 0007), so without a limit anyone could spend the API key. Counted per username, not
/// per session, so logging in again does not reset it. ASP.NET Core's own rate limiter (a fixed window per partition).
/// </summary>
public sealed class RunLimiter(ModelOptions options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(username =>
        RateLimitPartition.GetFixedWindowLimiter(username, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.RunsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    /// <summary>True when this user may start a run now; the run counts at once.</summary>
    public bool TryStart(string username)
    {
        using var lease = limiter.AttemptAcquire(username);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
