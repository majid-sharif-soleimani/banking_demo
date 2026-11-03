using System.Net;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.Api.Features.Loans;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class DisbursementTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<LoanResponse> DisburseAsync(LendingApp app, Guid applicationId)
    {
        var response = await app.ActAsync(applicationId, "disburse");
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LoanResponse>(Ct))!;
    }

    [Fact]
    public async Task Disbursing_creates_the_loan_with_its_schedule_and_publishes_LoanDisbursed()
    {
        // CAR 200,000 / 36 months, disbursed 15 Jan 2026: the stage 3 schedule, first item due 15 Feb (1,158.33 + 5,012.52).
        await using var app = await LendingApp.StartAsync(postgres);
        var application = await app.ApprovedAsync();

        var loan = await DisburseAsync(app, application.Id);

        loan.ShouldSatisfyAllConditions(
            l => l.Status.ShouldBe("Active"),
            l => l.Classification.ShouldBe("Current"),
            l => l.DisbursedOn.ShouldBe(LendingApp.Today),
            l => l.Schedule.Count.ShouldBe(36),
            l => l.OutstandingPrincipal.ShouldBe(new Money(200_000m, "SEK")),
            l => l.DaysPastDue.ShouldBe(0));
        loan.Schedule[0].ShouldSatisfyAllConditions(
            i => i.DueDate.ShouldBe(new DateOnly(2026, 2, 15)),
            i => i.InterestDue.ShouldBe(new Money(1_158.33m, "SEK")),
            i => i.PrincipalDue.ShouldBe(new Money(5_012.52m, "SEK")));
        var disbursed = await ApplicationTests.GetPublishedAsync<LoanDisbursed>(app);
        disbursed.ShouldSatisfyAllConditions(
            e => e.LoanId.ShouldBe(loan.Id),
            e => e.RepaymentAccountId.ShouldBe(app.AccountId),
            e => e.Principal.ShouldBe(new Money(200_000m, "SEK")));
        var after = await app.GetJsonAsync<ApplicationResponse>($"/loan-applications/{application.Id}");
        after.Status.ShouldBe("Disbursed");
        after.LoanId.ShouldBe(loan.Id);
    }

    [Fact]
    public async Task A_staged_loan_gets_its_agreed_stages()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var sek = (decimal v) => new Money(v, "SEK");
        var application = await app.ApprovedAsync("PURCHASE", 90_000m, 12,
            [new(new DateOnly(2026, 4, 15), sek(30_000m)), new(new DateOnly(2026, 7, 15), sek(30_000m)), new(new DateOnly(2026, 10, 15), sek(30_000m))]);

        var loan = await DisburseAsync(app, application.Id);

        // Stage 1: 90,000 × 0.085 × 90 / 365 = 1,886.30
        loan.Schedule.Select(i => i.PrincipalDue.Amount).ShouldBe([30_000m, 30_000m, 30_000m]);
        loan.Schedule[0].InterestDue.ShouldBe(sek(1_886.30m));
    }

    [Fact]
    public async Task Disbursing_twice_is_409_and_creates_one_loan()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var application = await app.ApprovedAsync();
        await DisburseAsync(app, application.Id);

        (await app.ActAsync(application.Id, "disburse")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await app.ScalarAsync<long>("SELECT count(*) FROM loan")).ShouldBe(1);
    }

    [Fact]
    public async Task Only_an_approved_application_can_be_disbursed_and_not_by_a_customer()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var submitted = await app.SubmitOkAsync();
        var approved = await app.ApprovedAsync();

        (await app.ActAsync(submitted.Id, "disburse")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await app.Client(customerId: app.CustomerId).PostAsJsonAsync($"/loan-applications/{approved.Id}/disburse", new { }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Loans_are_listed_per_customer()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var loan = await DisburseAsync(app, (await app.ApprovedAsync()).Id);

        (await app.GetJsonAsync<LoanResponse[]>($"/loans?customerId={app.CustomerId}")).ShouldHaveSingleItem().Id.ShouldBe(loan.Id);
        (await app.Client(customerId: Guid.NewGuid()).GetAsync($"/loans/{loan.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_list_running_loans_by_classification_and_customers_cannot()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var overdue = await DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.ScalarAsync<int>($"UPDATE loan SET classification = 'Overdue' WHERE id = '{overdue.Id}' RETURNING 1");

        var list = await app.Client(performedBy: "admin").GetFromJsonAsync<LoanResponse[]>("/loans?classification=Overdue", Ct);
        var asCustomer = await app.Client(customerId: app.CustomerId).GetAsync("/loans?classification=Overdue", Ct);
        var unknown = await app.Client(performedBy: "admin").GetAsync("/loans?classification=Late", Ct);

        list!.ShouldHaveSingleItem().Id.ShouldBe(overdue.Id);
        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
