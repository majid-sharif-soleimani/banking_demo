using System.Net;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class SimulationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Sek(decimal amount) => new(amount, "SEK");

    private static Task<HttpResponseMessage> SimulateAsync(
        LendingApp app, string product, Money amount, int term, IReadOnlyList<StageRequest>? stages = null, Guid? customerId = null) =>
        app.Client(performedBy: customerId is null ? "staff-1" : null, customerId: customerId)
            .PostAsJsonAsync($"/loan-products/{product}/simulation", new LoanSimulation.SimulationRequest(amount, term, stages), Ct);

    [Fact]
    public async Task A_car_loan_simulation_matches_the_hand_calculated_schedule()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var response = await SimulateAsync(app, "CAR", Sek(200_000m), 36);

        // Same figures as LoanCostTests / AmortizationAnnuityTests: A = 6,170.85, interest 22,150.54 over 36 months.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var simulation = (await response.Content.ReadFromJsonAsync<SimulationResponse>(Ct))!;
        simulation.Installment.ShouldBe(Sek(6_170.85m));
        simulation.TotalInterest.ShouldBe(Sek(22_150.54m));
        simulation.TotalCost.ShouldBe(Sek(222_150.54m));
        simulation.AnnualRate.ShouldBe(0.0695m);
        simulation.DisbursedOn.ShouldBe(LendingApp.Today);
        simulation.Schedule.Count.ShouldBe(36);
        simulation.Schedule[0].ShouldBe(new SimulatedItem(1, new DateOnly(2026, 2, 15), Sek(5_012.52m), Sek(1_158.33m), Sek(6_170.85m)));
        simulation.Schedule.Sum(i => i.Principal.Amount).ShouldBe(200_000m);
    }

    [Fact]
    public async Task A_customer_may_simulate_and_nothing_is_stored_or_published()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        await app.OutboxDrainedAsync();
        var outboxBefore = await app.ScalarAsync<long>("SELECT count(*) FROM outbox_message");

        var response = await SimulateAsync(app, "MORTGAGE", Sek(2_000_000m), 360, customerId: app.CustomerId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SimulationResponse>(Ct))!.TotalInterest.ShouldBe(Sek(1_541_966.48m));
        (await app.ScalarAsync<long>("SELECT count(*) FROM loan_application")).ShouldBe(0);
        (await app.ScalarAsync<long>("SELECT count(*) FROM loan")).ShouldBe(0);
        (await app.ScalarAsync<long>("SELECT count(*) FROM outbox_message")).ShouldBe(outboxBefore);
    }

    [Fact]
    public async Task A_staged_simulation_has_no_single_installment()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var response = await SimulateAsync(app, "PURCHASE", Sek(100_000m), 12,
            [new(new DateOnly(2026, 4, 15), Sek(30_000m)), new(new DateOnly(2026, 7, 15), Sek(30_000m)), new(new DateOnly(2026, 10, 15), Sek(40_000m))]);

        // Actual/365 per stage: 2,095.89 + 1,483.42 + 856.99 = 4,436.30 (LoanCostTests).
        var simulation = (await response.Content.ReadFromJsonAsync<SimulationResponse>(Ct))!;
        simulation.Installment.ShouldBeNull();
        simulation.TotalInterest.ShouldBe(Sek(4_436.30m));
        simulation.Schedule.Select(i => i.Total.Amount).ShouldBe([32_095.89m, 31_483.42m, 40_856.99m]);
    }

    [Theory]
    [InlineData("CAR", 19_999, "SEK", 36, "amount_out_of_range")]
    [InlineData("CAR", 200_000, "EUR", 36, "amount_out_of_range")]
    [InlineData("CAR", 200_000, "SEK", 85, "term_out_of_range")]
    [InlineData("PURCHASE", 100_000, "SEK", 12, "stage_plan_required")]
    public async Task A_simulation_outside_the_product_is_rejected(string product, decimal amount, string currency, int term, string code)
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var response = await SimulateAsync(app, product, new Money(amount, currency), term);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApplicationTests.Code(response)).ShouldBe(code);
    }

    [Fact]
    public async Task An_invalid_stage_plan_and_an_unknown_product_are_rejected()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var tooMany = await SimulateAsync(app, "PURCHASE", Sek(100_000m), 12,
            [.. Enumerable.Range(1, 5).Select(m => new StageRequest(LendingApp.Today.AddMonths(m), Sek(20_000m)))]);
        var unknown = await SimulateAsync(app, "BOAT", Sek(100_000m), 12);

        (await ApplicationTests.Code(tooMany)).ShouldBe("too_many_stages");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApplicationTests.Code(unknown)).ShouldBe("product_not_found");
    }
}
