using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features.Applications;
using Bank.Testing;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Lending.IntegrationTests.Support;

/// <summary>
/// The real Lending service on a fresh schema with an in-memory broker. <see cref="StartAsync"/> delivers what Lending
/// would have received: the business date, SEK active, one customer and that customer's SEK repayment account.
/// </summary>
public sealed class LendingApp(string connectionString, Action<IServiceCollection>? configure)
    : BankServiceApp<Program>(connectionString, new Dictionary<string, string?> { ["ConnectionStrings:lending-db"] = connectionString }, configure)
{
    public static readonly DateOnly Today = new(2026, 1, 15);

    public Guid CustomerId { get; } = Guid.CreateVersion7();

    public Guid AccountId { get; } = Guid.CreateVersion7();

    public DateOnly BusinessDate { get; private set; } = Today;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<LendingApp> StartAsync(PostgresFixture postgres, Action<IServiceCollection>? configure = null)
    {
        var app = new LendingApp(await postgres.CreateServiceSchemaAsync("lending"), configure);
        app.Start();
        await app.OpenBusinessDayAsync(Today);
        await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), Today, "system", "SEK", 2), "SEK");
        await app.DeliverAsync(new CustomerRegistered(Guid.NewGuid(), Today, "system", app.CustomerId, "Ada Lovelace", "en"), app.CustomerId.ToString());
        await app.DeliverAsync(new AccountOpened(Guid.NewGuid(), Today, "system", app.AccountId, "123456782", app.CustomerId, "Current", "SEK"), app.AccountId.ToString());
        return app;
    }

    public async Task MoveToAsync(DateOnly date)
    {
        BusinessDate = date;
        await OpenBusinessDayAsync(date);
    }

    /// <summary>Opens the business date and runs its end of day, then waits until every event and answer has gone through.</summary>
    public async Task RunDayAsync(DateOnly date)
    {
        await MoveToAsync(date);
        await DeliverAsync(new DayClosed(Guid.NewGuid(), date, "system", date), date.ToString("yyyy-MM-dd"));
        await OutboxDrainedAsync();
    }

    public Task<HttpResponseMessage> SubmitAsync(string product = "CAR", decimal amount = 200_000m, int term = 36, IReadOnlyList<StageRequest>? stages = null, Guid? account = null) =>
        PostJsonAsync("/loan-applications", new ApplicationEndpoints.SubmitRequest(
            CustomerId, product, new Money(amount, "SEK"), term, account ?? AccountId, "A car", stages));

    public async Task<ApplicationResponse> SubmitOkAsync(string product = "CAR", decimal amount = 200_000m, int term = 36, IReadOnlyList<StageRequest>? stages = null)
    {
        var response = await SubmitAsync(product, amount, term, stages);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>(Ct))!;
    }

    public Task<HttpResponseMessage> ActAsync(Guid applicationId, string action, object? body = null) =>
        CreateClient().PostAsJsonAsync($"/loan-applications/{applicationId}/{action}", body ?? new { }, Ct);

    /// <summary>Submitted → UnderReview → Approved.</summary>
    public async Task<ApplicationResponse> ApprovedAsync(string product = "CAR", decimal amount = 200_000m, int term = 36, IReadOnlyList<StageRequest>? stages = null)
    {
        var application = await SubmitOkAsync(product, amount, term, stages);
        (await ActAsync(application.Id, "review")).EnsureSuccessStatusCode();
        (await ActAsync(application.Id, "approve")).EnsureSuccessStatusCode();
        return application;
    }
}
