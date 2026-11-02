using System.Net;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features.Loans;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class PayoffTests(PostgresFixture postgres)
{
    private static readonly DateOnly Feb15 = new(2026, 2, 15);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Sek(decimal amount) => new(amount, "SEK");

    /// <summary>CAR 200,000/36 disbursed 15 Jan; item 1 collected on 15 Feb; the business date is then 1 Mar.</summary>
    private static async Task<(LendingApp App, FakeAccounts Accounts, LoanResponse Loan)> AfterFirstInstallmentAsync(PostgresFixture postgres, decimal balance)
    {
        var app = await LendingApp.StartAsync(postgres);
        var accounts = new FakeAccounts(app, balance);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.RunDayAsync(Feb15);
        await app.MoveToAsync(Mar1);
        return (app, accounts, loan);
    }

    [Fact]
    public async Task The_quote_matches_the_stage_3_hand_calculation()
    {
        // EarlyPayoffTests.Quote_CarMidPeriod: total 197,514.49 = 194,987.48 + 527.01 + minimum 2,000; waived 18,465.20.
        var (app, _, loan) = await AfterFirstInstallmentAsync(postgres, 1_000_000m);
        await using var _ = app;

        var quote = await app.GetJsonAsync<PayoffQuoteResponse>($"/loans/{loan.Id}/payoff-quote?date={Mar1:yyyy-MM-dd}");

        quote.ShouldSatisfyAllConditions(
            q => q.OutstandingPrincipal.ShouldBe(Sek(194_987.48m)),
            q => q.InterestToDate.ShouldBe(Sek(527.01m)),
            q => q.MinimumInterest.ShouldBe(Sek(2_000m)),
            q => q.WaivedInterest.ShouldBe(Sek(18_465.20m)),
            q => q.Total.ShouldBe(Sek(197_514.49m)));
    }

    [Fact]
    public async Task A_payoff_with_enough_money_settles_the_loan_early()
    {
        var (app, accounts, loan) = await AfterFirstInstallmentAsync(postgres, 1_000_000m);
        await using var _ = app;

        var response = await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await app.OutboxDrainedAsync();

        accounts.Debits[^1].ShouldBe(Sek(197_514.49m));
        var after = await app.GetJsonAsync<LoanResponse>($"/loans/{loan.Id}");
        after.Status.ShouldBe("PaidOffEarly");
        after.Schedule.ShouldAllBe(i => i.Status == "Paid");
        after.OutstandingPrincipal.ShouldBe(Sek(0m));
        var early = app.Published<LoanPaidOffEarly>().ShouldHaveSingleItem();
        early.Amount.ShouldBe(Sek(197_514.49m));
        early.WaivedInterest.ShouldBe(Sek(18_465.20m));
        // Split for the ledger: principal 194,987.48, interest 527.01 + 2,000 = 2,527.01, no penalty.
        var split = app.Published<LoanPaymentAllocated>().Single(a => a.Source == "Payoff");
        split.ShouldSatisfyAllConditions(s => s.Principal.ShouldBe(Sek(194_987.48m)), s => s.Interest.ShouldBe(Sek(2_527.01m)), s => s.Penalty.ShouldBe(Sek(0m)));
    }

    [Fact]
    public async Task A_payoff_with_too_little_money_leaves_the_loan_active_and_can_be_tried_again()
    {
        var (app, accounts, loan) = await AfterFirstInstallmentAsync(postgres, 10_000m);
        await using var _ = app;
        accounts.Balance = 1_000m;

        (await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await app.OutboxDrainedAsync();

        (await app.GetJsonAsync<LoanResponse>($"/loans/{loan.Id}")).Status.ShouldBe("Active");
        (await app.ScalarAsync<bool>($"SELECT pending_payoff_collection_id IS NULL FROM loan WHERE id = '{loan.Id}'")).ShouldBeTrue();
        (await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task No_second_payoff_while_one_is_pending_and_none_for_a_closed_loan()
    {
        // No fake Accounts: the request stays unanswered.
        await using var app = await LendingApp.StartAsync(postgres);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        (await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var second = await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApplicationTests.Code(second)).ShouldBe("collection_pending");
    }

    [Fact]
    public async Task A_quote_for_a_past_date_is_rejected()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);

        (await app.CreateClient().GetAsync($"/loans/{loan.Id}/payoff-quote?date=2026-01-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_end_of_day_while_a_payoff_is_pending_neither_charges_penalty_nor_asks_for_installments()
    {
        // Review of 2026-10-31: the payoff settles exactly the quote it was requested with.
        await using var app = await LendingApp.StartAsync(postgres);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.MoveToAsync(Feb15.AddDays(10)); // item 1 is 10 days overdue, past grace
        var quote = (await (await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct)).Content.ReadFromJsonAsync<PayoffQuoteResponse>(Ct))!;

        await app.RunDayAsync(Feb15.AddDays(10));

        app.Published<InstallmentDue>().ShouldBeEmpty();
        app.Published<PenaltyAccrued>().ShouldBeEmpty();
        await app.DeliverAsync(new PaymentCollected(Guid.NewGuid(), Feb15.AddDays(11), "system", loan.Id, quote.CollectionId!.Value, null, "Payoff", quote.Total), "k");
        await app.OutboxDrainedAsync();
        (await app.GetJsonAsync<LoanResponse>($"/loans/{loan.Id}")).Status.ShouldBe("PaidOffEarly");
        var split = app.Published<LoanPaymentAllocated>().Single(a => a.Source == "Payoff");
        (split.Principal + split.Interest + split.Penalty).ShouldBe(quote.Total);
        split.Interest.ShouldBe(quote.InterestToDate + quote.MinimumInterest);
    }

    [Fact]
    public async Task A_collection_for_a_settled_loan_is_never_booked_silently()
    {
        var (app, _, loan) = await AfterFirstInstallmentAsync(postgres, 1_000_000m);
        await using var _a = app;
        await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct);
        await app.OutboxDrainedAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => app.DeliverAsync(
            new PaymentCollected(Guid.NewGuid(), Mar1, "system", loan.Id, Guid.NewGuid(), 2, "Installment", Sek(6_170.85m)), "k"));
    }
}
