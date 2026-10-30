using Aspire.Hosting;
using Aspire.Hosting.Testing;

[assembly: AssemblyFixture(typeof(Bank.Platform.IntegrationTests.PlatformFixture))]

namespace Bank.Platform.IntegrationTests;

/// <summary>
/// Starts the real AppHost once for all tests, with the same containers, init script and generated secret parameters
/// as <c>dotnet run</c>. The tests read credentials only through the AppHost's connection strings.
/// </summary>
public sealed class PlatformFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);

        // The dashboard is not needed to test the infrastructure, and starting it only slows the tests down.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Bank_AppHost>(
            ["--DcpPublisher:RandomizePorts=true", "--DisableDashboard=true"],
            timeout.Token);

        App = await builder.BuildAsync(timeout.Token);
        await App.StartAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    public async Task<string> ConnectionStringAsync(string resourceName) =>
        await App.GetConnectionStringAsync(resourceName, TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException($"The AppHost has no connection string named '{resourceName}'.");
}
