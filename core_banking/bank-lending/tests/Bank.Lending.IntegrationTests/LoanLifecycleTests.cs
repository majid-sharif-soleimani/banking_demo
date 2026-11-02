using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.Api.Features.Loans;
using Bank.Lending.IntegrationTests.Support;
using Bank.Messaging.Inbox;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class LoanLifecycleTests(PostgresFixture postgres)
{
    private static readonly DateOnly Feb15 = new(2026, 2, 15);

    private static Money Sek(decimal amount) => new(amount, "SEK");

    private static async Task<(LendingApp App, FakeAccounts Accounts, LoanResponse Loan)> CarLoanAsync(PostgresFixture postgres, decimal balance)
    {
        var app = await LendingApp.StartAsync(postgres);
        var accounts = new FakeAccounts(app, balance);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        return (app, accounts, loan);
    }

    private static Task<LoanResponse> LoanAsync(LendingApp app, Guid id) => app.GetJsonAsync<LoanResponse>($"/loans/{id}");

    [Fact]
    public async Task An_annuity_loan_is_collected_on_its_due_dates_for_three_months()
    {
        var (app, accounts, loan) = await CarLoanAsync(postgres, 1_000_000m);
        await using var _ = app;

        foreach (var due in new[] { Feb15, Feb15.AddMonths(1), Feb15.AddMonths(2) })
        {
            await app.RunDayAsync(due);
        }

        var after = await LoanAsync(app, loan.Id);
        after.Schedule.Take(3).ShouldAllBe(i => i.Status == "Paid");
        after.Schedule.Skip(3).ShouldAllBe(i => i.Status == "Unpaid");
        accounts.Debits.ShouldAllBe(d => d.Amount == 6_170.85m);
        accounts.Debits.Count.ShouldBe(3);
        // 200,000 − (5,012.52 + 5,041.55 + 5,070.75) = 184,875.18
        after.OutstandingPrincipal.ShouldBe(Sek(184_875.18m));
        after.Classification.ShouldBe("Current");
        var allocated = app.Published<LoanPaymentAllocated>();
        allocated.Count.ShouldBe(3);
        allocated[0].ShouldSatisfyAllConditions(a => a.Source.ShouldBe("Account"), a => a.Interest.ShouldBe(Sek(1_158.33m)), a => a.Principal.ShouldBe(Sek(5_012.52m)));
        app.Published<DayCompleted>().ShouldAllBe(d => d.Service == "lending");
    }

    [Fact]
    public async Task Insufficient_balance_makes_the_item_overdue_with_penalty_from_day_6_until_it_is_paid()
    {
        var (app, accounts, loan) = await CarLoanAsync(postgres, 0m);
        await using var _ = app;

        // Due 15 Feb, unpaid: asked again every day; grace days 16–20 Feb without penalty.
        for (var day = Feb15; day <= Feb15.AddDays(5); day = day.AddDays(1))
        {
            await app.RunDayAsync(day);
        }

        (await LoanAsync(app, loan.Id)).ShouldSatisfyAllConditions(
            l => l.Schedule[0].PenaltyDue.ShouldBe(Sek(0m)), l => l.Classification.ShouldBe("Overdue"), l => l.DaysPastDue.ShouldBe(5));
        app.Published<InstallmentDue>().Count.ShouldBe(6); // 15–20 Feb, one open request at a time
        app.Published<LoanClassificationChanged>().ShouldHaveSingleItem().ToClassification.ShouldBe("Overdue");

        // Day 6 (21 Feb): 6,170.85 × 0.0005 = 3.09 penalty.
        await app.RunDayAsync(Feb15.AddDays(6));
        (await LoanAsync(app, loan.Id)).Schedule[0].PenaltyDue.ShouldBe(Sek(3.09m));
        app.Published<PenaltyAccrued>().ShouldHaveSingleItem().Amount.ShouldBe(Sek(3.09m));

        // The customer deposits; 22 Feb asks for 6,170.85 + 3.09 = 6,173.94 and gets it. That day's penalty (3.09) was charged
        // in the same end of day, before the answer came, so 3.09 of penalty is left; 23 Feb collects it.
        accounts.Balance = 10_000m;
        await app.RunDayAsync(Feb15.AddDays(7));
        accounts.Debits.ShouldHaveSingleItem().ShouldBe(Sek(6_173.94m));
        (await LoanAsync(app, loan.Id)).Schedule[0].ShouldSatisfyAllConditions(i => i.PenaltyDue.ShouldBe(Sek(6.18m)), i => i.Status.ShouldBe("PartiallyPaid"));

        await app.RunDayAsync(Feb15.AddDays(8));
        var paid = await LoanAsync(app, loan.Id);
        paid.Schedule[0].Status.ShouldBe("Paid");
        accounts.Debits[^1].ShouldBe(Sek(3.09m));

        // The next end of day sees nothing past due: back to Current.
        await app.RunDayAsync(Feb15.AddDays(9));
        (await LoanAsync(app, loan.Id)).Classification.ShouldBe("Current");
    }

    [Fact]
    public async Task An_unpaid_loan_becomes_Default_after_90_days()
    {
        var (app, _, loan) = await CarLoanAsync(postgres, 0m);
        await using var _app = app;

        await app.RunDayAsync(Feb15.AddDays(1));   // 1 day past due → Overdue
        await app.RunDayAsync(Feb15.AddDays(89));  // still Overdue
        await app.RunDayAsync(Feb15.AddDays(90));  // 90 days → Default

        var changes = app.Published<LoanClassificationChanged>();
        changes.Select(c => c.ToClassification).ShouldBe(["Overdue", "Default"]);
        changes[^1].DaysPastDue.ShouldBe(90);
        (await LoanAsync(app, loan.Id)).Classification.ShouldBe("Default");
    }

    [Fact]
    public async Task A_staged_loan_with_three_stages_is_collected_stage_by_stage_and_paid_off()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var accounts = new FakeAccounts(app, 1_000_000m);
        var stages = new[] { new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 15), new DateOnly(2026, 10, 15) };
        var loan = await DisbursementTests.DisburseAsync(app,
            (await app.ApprovedAsync("PURCHASE", 90_000m, 12, [.. stages.Select(d => new StageRequest(d, Sek(30_000m)))])).Id);

        foreach (var stage in stages)
        {
            await app.RunDayAsync(stage);
        }

        var after = await LoanAsync(app, loan.Id);
        after.Status.ShouldBe("PaidOff");
        after.Schedule.ShouldAllBe(i => i.Status == "Paid");
        // Stage interest: 90,000 × 0.085 × 90/365 = 1,886.30; 60,000 × 0.085 × 91/365 = 1,271.51; 30,000 × 0.085 × 92/365 = 642.74
        accounts.Debits.Select(d => d.Amount).ShouldBe([31_886.30m, 31_271.51m, 30_642.74m]);
    }

    [Fact]
    public async Task The_same_end_of_day_twice_charges_and_asks_once()
    {
        var (app, _, loan) = await CarLoanAsync(postgres, 0m);
        await using var _app = app;
        var day6 = Feb15.AddDays(6);
        await app.RunDayAsync(day6);

        await app.DeliverAsync(new DayClosed(Guid.NewGuid(), day6, "system", day6), "again");
        await app.OutboxDrainedAsync();

        (await LoanAsync(app, loan.Id)).Schedule[0].PenaltyDue.ShouldBe(Sek(3.09m));
        app.Published<DayCompleted>().ShouldHaveSingleItem();
        app.Published<InstallmentDue>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_PaymentCollected_delivered_twice_is_allocated_once()
    {
        var (app, _, loan) = await CarLoanAsync(postgres, 0m);
        await using var _app = app;
        await app.RunDayAsync(Feb15);
        var due = app.Published<InstallmentDue>().ShouldHaveSingleItem();
        var collected = new PaymentCollected(Guid.NewGuid(), Feb15, "system", loan.Id, due.CollectionId, 1, "Installment", due.Amount);

        // The fake's PaymentFailed already closed this collection; a late PaymentCollected for it still allocates exactly once.
        (await app.DeliverAsync(collected, "k")).ShouldBe(ProcessOutcome.Handled);
        await app.DeliverAsync(collected with { EventId = Guid.NewGuid() }, "k");

        (await app.ScalarAsync<long>("SELECT count(*) FROM loan_payment")).ShouldBe(1);
        (await LoanAsync(app, loan.Id)).Schedule[0].Status.ShouldBe("Paid");
    }

    [Fact]
    public async Task A_captured_card_payment_pays_the_overdue_installment_with_source_Card()
    {
        // Stage 6: Cards publishes CardPaymentCaptured; Lending allocates it like an installment payment, oldest due first.
        var (app, _, loan) = await CarLoanAsync(postgres, 0m);
        await using var _app = app;
        await app.RunDayAsync(Feb15);
        await app.MoveToAsync(Feb15.AddDays(3));
        var paymentId = Guid.NewGuid();

        await app.DeliverAsync(new CardPaymentCaptured(Guid.NewGuid(), Feb15.AddDays(3), "customer", paymentId, loan.Id, 1, Sek(6_170.85m)), loan.Id.ToString());
        await app.OutboxDrainedAsync();

        (await LoanAsync(app, loan.Id)).Schedule[0].Status.ShouldBe("Paid");
        var allocated = app.Published<LoanPaymentAllocated>().ShouldHaveSingleItem();
        allocated.ShouldSatisfyAllConditions(a => a.Source.ShouldBe("Card"), a => a.PaymentId.ShouldBe(paymentId), a => a.Principal.ShouldBe(Sek(5_012.52m)));
    }

    [Fact]
    public async Task A_card_payment_for_a_future_installment_prepays_it()
    {
        // Item 2 is not due yet. Since Majid's review of 2026-11-02 money for a later item prepays it (ADR 0003, point 5):
        // 6,170.85 covers item 2 in full, so nothing is left to give back.
        var (app, _, loan) = await CarLoanAsync(postgres, 0m);
        await using var _app = app;

        await app.DeliverAsync(new CardPaymentCaptured(Guid.NewGuid(), LendingApp.Today, "customer", Guid.NewGuid(), loan.Id, 2, Sek(6_170.85m)), loan.Id.ToString());

        (await LoanAsync(app, loan.Id)).Schedule[1].Status.ShouldBe("Paid");
        (await app.ScalarAsync<decimal>("SELECT unallocated FROM loan_payment")).ShouldBe(0m);
        await app.OutboxDrainedAsync();
        app.Published<LoanPaymentAllocated>().ShouldHaveSingleItem().Unallocated.ShouldBeNull(); // optional field, set only when money is left over
    }
}
