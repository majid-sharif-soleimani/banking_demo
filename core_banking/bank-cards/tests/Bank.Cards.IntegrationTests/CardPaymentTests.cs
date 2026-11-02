using System.Net;
using System.Net.Http.Json;
using Bank.Cards.Api.Features;
using Bank.Cards.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Cards.IntegrationTests;

public class CardPaymentTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PayAsync(CardsApp app, string card, decimal amount = 6_170.85m, string? key = null, Guid? loan = null, int? item = 1) =>
        app.PostJsonAsync("/card-payments", new CardPayments.Request(card, new Money(amount, "SEK"), "LoanInstallment", loan ?? app.LoanId, item), key ?? Guid.NewGuid().ToString());

    private static async Task<string> Code(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;

    [Fact]
    public async Task The_approve_card_captures_and_publishes_CardPaymentCaptured()
    {
        await using var app = await CardsApp.StartAsync(postgres);

        var response = await PayAsync(app, "9999 0000 0000 0001");

        var payment = (await response.Content.ReadFromJsonAsync<CardPaymentResponse>(Ct))!;
        payment.ShouldSatisfyAllConditions(p => p.Status.ShouldBe("Captured"), p => p.Card.ShouldBe("************0001"));
        await Eventually.AssertAsync(() =>
        {
            var captured = app.Published<CardPaymentCaptured>().ShouldHaveSingleItem();
            captured.ShouldSatisfyAllConditions(e => e.PaymentId.ShouldBe(payment.Id), e => e.LoanId.ShouldBe(app.LoanId),
                e => e.ScheduleItemNumber.ShouldBe(1), e => e.Amount.ShouldBe(new Money(6_170.85m, "SEK")));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("9999000000000002", "declined")]
    [InlineData("9999000000000003", "insufficient_funds")]
    public async Task The_decline_cards_publish_CardPaymentDeclined_with_their_reason(string card, string reason)
    {
        await using var app = await CardsApp.StartAsync(postgres);

        var payment = await (await PayAsync(app, card)).Content.ReadFromJsonAsync<CardPaymentResponse>(Ct);

        payment!.Status.ShouldBe("Declined");
        payment.Reason.ShouldBe(reason);
        await Eventually.AssertAsync(() =>
        {
            var declined = app.Published<CardPaymentDeclined>().ShouldHaveSingleItem();
            declined.Reason.ShouldBe(reason);
            declined.CustomerId.ShouldBe(app.CustomerId);
            return Task.CompletedTask;
        });
        app.Published<CardPaymentCaptured>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_cards_and_loans_are_rejected_with_422()
    {
        await using var app = await CardsApp.StartAsync(postgres);

        (await Code(await PayAsync(app, "4111111111111111"))).ShouldBe("unknown_card");
        (await Code(await PayAsync(app, "9999000000000001", loan: Guid.NewGuid()))).ShouldBe("loan_not_found");
    }

    [Fact]
    public async Task A_repeated_key_returns_the_same_payment_and_captures_once()
    {
        await using var app = await CardsApp.StartAsync(postgres);

        var first = await (await PayAsync(app, "9999000000000001", key: "pay-1")).Content.ReadFromJsonAsync<CardPaymentResponse>(Ct);
        var second = await (await PayAsync(app, "9999000000000001", key: "pay-1")).Content.ReadFromJsonAsync<CardPaymentResponse>(Ct);
        var other = await PayAsync(app, "9999000000000001", amount: 1m, key: "pay-1");

        second!.Id.ShouldBe(first!.Id);
        (await Code(other)).ShouldBe("idempotency_key_reused");
        await app.OutboxDrainedAsync();
        app.Published<CardPaymentCaptured>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Another_customers_loan_is_not_found_and_amounts_must_fit_the_loan()
    {
        await using var app = await CardsApp.StartAsync(postgres);

        var foreign = await app.Client(customerId: Guid.NewGuid(), idempotencyKey: "k").PostAsJsonAsync("/card-payments",
            new CardPayments.Request("9999000000000001", new Money(10m, "SEK"), "LoanInstallment", app.LoanId, null), Ct);

        (await Code(foreign)).ShouldBe("loan_not_found");
        (await Code(await app.PostJsonAsync("/card-payments", new CardPayments.Request("9999000000000001", new Money(10m, "EUR"), "LoanInstallment", app.LoanId, null), "k2")))
            .ShouldBe("invalid_amount");
        (await Code(await PayAsync(app, "9999000000000001", amount: 10.001m))).ShouldBe("invalid_amount");
    }
}
