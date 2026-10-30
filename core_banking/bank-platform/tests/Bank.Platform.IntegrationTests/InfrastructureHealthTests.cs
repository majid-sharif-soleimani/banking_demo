using Aspire.Hosting;

namespace Bank.Platform.IntegrationTests;

public sealed class InfrastructureHealthTests(PlatformFixture platform)
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(3);

    [Theory]
    [InlineData("postgres")]
    [InlineData("bank")]
    [InlineData("kafka")]
    public async Task Resource_becomes_healthy(string resourceName)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(HealthTimeout);

        // Throws when the timeout passes first, for example when the init script failed and PostgreSQL never started.
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, timeout.Token);
    }
}
