using System.Net;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Lending.IntegrationTests;

public class ApplicationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;

    private static async Task<ApplicationResponse> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApplicationResponse>(Ct))!;

    [Fact]
    public async Task The_three_seeded_products_are_listed()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var products = await app.GetJsonAsync<List<Dictionary<string, object>>>("/loan-products");

        products.Select(p => p["code"].ToString()).ShouldBe(["CAR", "MORTGAGE", "PURCHASE"]);
    }

    [Fact]
    public async Task Submitting_creates_a_Submitted_application_with_history_and_publishes_LoanApplicationSubmitted()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var application = await app.SubmitOkAsync();

        application.Status.ShouldBe("Submitted");
        application.History.ShouldHaveSingleItem().ToStatus.ShouldBe("Submitted");
        await Eventually.AssertAsync(() =>
        {
            var submitted = app.Published<LoanApplicationSubmitted>().ShouldHaveSingleItem();
            submitted.ProductCode.ShouldBe("CAR");
            submitted.RequestedAmount.ShouldBe(new Money(200_000m, "SEK"));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("CAR", 19_999, 36, "amount_out_of_range")]
    [InlineData("CAR", 600_001, 36, "amount_out_of_range")]
    [InlineData("CAR", 200_000, 11, "term_out_of_range")]
    [InlineData("MORTGAGE", 500_000, 361, "term_out_of_range")]
    [InlineData("PURCHASE", 100_000, 12, "stage_plan_required")]
    [InlineData("BOAT", 100_000, 12, "product_not_found")]
    public async Task Applications_outside_the_product_are_rejected(string product, decimal amount, int term, string code)
    {
        await using var app = await LendingApp.StartAsync(postgres);

        (await Code(await app.SubmitAsync(product, amount, term))).ShouldBe(code);
    }

    [Fact]
    public async Task A_staged_product_accepts_a_valid_plan_and_rejects_an_invalid_one()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var sek = (decimal v) => new Money(v, "SEK");

        var ok = await app.SubmitAsync("PURCHASE", 100_000m, 12,
            [new(new DateOnly(2026, 4, 15), sek(30_000m)), new(new DateOnly(2026, 7, 15), sek(30_000m)), new(new DateOnly(2026, 10, 15), sek(40_000m))]);
        var tooMany = await app.SubmitAsync("PURCHASE", 100_000m, 12,
            [.. Enumerable.Range(1, 5).Select(m => new StageRequest(new DateOnly(2026, 1, 15).AddMonths(m), sek(20_000m)))]);

        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Read(ok)).Stages!.Count.ShouldBe(3);
        (await Code(tooMany)).ShouldBe("too_many_stages");
        (await Code(await app.SubmitAsync("CAR", 200_000m, 36, [new(new DateOnly(2026, 4, 15), sek(200_000m))]))).ShouldBe("stages_not_allowed");
    }

    [Fact]
    public async Task The_repayment_account_must_be_the_customers_own_in_the_loan_currency()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var eur = Guid.NewGuid();
        await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), LendingApp.Today, "system", "EUR", 2), "EUR");
        await app.DeliverAsync(new AccountOpened(Guid.NewGuid(), LendingApp.Today, "system", eur, "100000017", app.CustomerId, "Current", "EUR"), eur.ToString());

        (await Code(await app.SubmitAsync(account: eur))).ShouldBe("repayment_account_invalid");
        (await Code(await app.SubmitAsync(account: Guid.NewGuid()))).ShouldBe("repayment_account_invalid");
    }

    [Fact]
    public async Task The_review_path_through_information_and_appointment_reaches_Approved()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.SubmitOkAsync()).Id;

        (await Read(await app.ActAsync(id, "review"))).Status.ShouldBe("UnderReview");
        (await Read(await app.ActAsync(id, "information-requests", new ApplicationEndpoints.InformationRequest(["Payslip", "ID copy"])))).Status.ShouldBe("InfoRequested");
        var requested = await GetPublishedAsync<InformationRequested>(app);
        requested.RequestedItems.ShouldBe("Payslip\nID copy");

        await app.DeliverAsync(new InformationReceived(Guid.NewGuid(), LendingApp.Today, "customer", requested.InformationRequestId, id), id.ToString());
        (await app.GetJsonAsync<ApplicationResponse>($"/loan-applications/{id}")).Status.ShouldBe("UnderReview");

        await app.DeliverAsync(new AppointmentScheduled(Guid.NewGuid(), LendingApp.Today, "E001", Guid.NewGuid(), id, "E001", DateTimeOffset.UtcNow), id.ToString());
        (await app.GetJsonAsync<ApplicationResponse>($"/loan-applications/{id}")).Status.ShouldBe("AppointmentScheduled");

        (await Read(await app.ActAsync(id, "review"))).Status.ShouldBe("UnderReview");
        var approved = await Read(await app.ActAsync(id, "approve"));

        approved.Status.ShouldBe("Approved");
        approved.History.Select(h => h.ToStatus).ShouldBe(
            ["Submitted", "UnderReview", "InfoRequested", "UnderReview", "AppointmentScheduled", "UnderReview", "Approved"]);
        await app.OutboxDrainedAsync();
        app.Published<LoanApplicationStatusChanged>().Count.ShouldBe(6);
    }

    [Theory]
    [InlineData("reject", "Rejected")]
    [InlineData("cancel", "Cancelled")]
    public async Task An_open_application_can_be_rejected_or_cancelled_with_a_reason(string action, string status)
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.SubmitOkAsync()).Id;
        await app.ActAsync(id, "review");

        var result = await Read(await app.ActAsync(id, action, new ApplicationEndpoints.ReasonRequest("Not now")));

        result.Status.ShouldBe(status);
        result.History[^1].Reason.ShouldBe("Not now");
    }

    [Theory]
    [InlineData("approve")]         // Submitted cannot be approved without review
    [InlineData("information-requests")]
    public async Task Illegal_steps_from_Submitted_return_409(string action)
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.SubmitOkAsync()).Id;

        var response = await app.ActAsync(id, action, new ApplicationEndpoints.InformationRequest(["x"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(response)).ShouldBe("invalid_transition");
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("review")]
    public async Task An_approved_application_cannot_go_back(string action)
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.ApprovedAsync()).Id;

        (await app.ActAsync(id, action, new ApplicationEndpoints.ReasonRequest("x"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_late_appointment_for_a_cancelled_application_is_ignored()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.SubmitOkAsync()).Id;
        await app.ActAsync(id, "cancel", new ApplicationEndpoints.ReasonRequest("Changed my mind"));

        await app.DeliverAsync(new AppointmentScheduled(Guid.NewGuid(), LendingApp.Today, "E001", Guid.NewGuid(), id, "E001", DateTimeOffset.UtcNow), id.ToString());

        (await app.GetJsonAsync<ApplicationResponse>($"/loan-applications/{id}")).Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Customers_cannot_review_but_can_cancel_their_own_application()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var id = (await app.SubmitOkAsync()).Id;
        var customer = app.Client(customerId: app.CustomerId);

        (await customer.PostAsJsonAsync($"/loan-applications/{id}/review", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.Client(customerId: Guid.NewGuid()).GetAsync($"/loan-applications/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await customer.PostAsJsonAsync($"/loan-applications/{id}/cancel", new ApplicationEndpoints.ReasonRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    internal static async Task<T> GetPublishedAsync<T>(LendingApp app) where T : IIntegrationEvent
    {
        T? found = default;
        await Eventually.AssertAsync(() =>
        {
            found = app.Published<T>().ShouldHaveSingleItem();
            return Task.CompletedTask;
        });
        return found!;
    }
}
