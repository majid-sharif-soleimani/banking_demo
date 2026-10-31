using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.StandingOrders;

public sealed record StandingOrderResponse(
    Guid Id, Guid FromAccountId, Guid ToAccountId, Money Amount, string Frequency, DateOnly StartDate, DateOnly? EndDate,
    DateOnly? NextRunDate, string Status)
{
    public static StandingOrderResponse From(StandingOrder o) =>
        new(o.Id, o.FromAccountId, o.ToAccountId, new Money(o.Amount, o.Currency), o.Frequency.ToString(), o.StartDate, o.EndDate,
            o.NextRunDate, o.Status.ToString());
}

/// <summary>
/// POST /standing-orders, GET /standing-orders/{id}, DELETE /standing-orders/{id} (cancel). An order can be created
/// without enough money; the balance is checked when it runs (design plan section 2). It runs at the end of its start
/// date at the earliest, so the start date may be today's business date but not earlier.
/// </summary>
public static class StandingOrderEndpoints
{
    public sealed record Request(Guid FromAccountId, Guid ToAccountId, Money Amount, string Frequency, DateOnly StartDate, DateOnly? EndDate);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/standing-orders", CreateAsync).WithName("CreateStandingOrder").WithTags("Standing orders");
        app.MapGet("/standing-orders/{id:guid}", GetAsync).WithName("GetStandingOrder").WithTags("Standing orders");
        app.MapDelete("/standing-orders/{id:guid}", CancelAsync).WithName("CancelStandingOrder").WithTags("Standing orders");
    }

    /// <summary>Idempotency-Key required: an order creates future money movement, and a retried POST must not create two.</summary>
    private static Task<IResult> CreateAsync(
        Request request, HttpRequest http, [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        AccountsDbContext db, IBusinessClock clock, TimeProvider time, CancellationToken ct) =>
        IdempotentCommand.RunAsync(http, db, $"standing-order:{request.FromAccountId}", request,
            async token => await CreateCoreAsync(request, http, performedBy, db, clock, time, token), ct);

    private static async Task<Outcome> CreateCoreAsync(
        Request request, HttpRequest http, string? performedBy, AccountsDbContext db, IBusinessClock clock, TimeProvider time, CancellationToken ct)
    {
        if (!Enum.TryParse<Frequency>(request.Frequency, ignoreCase: false, out var frequency) || !Enum.IsDefined(frequency))
        {
            return Outcome.Fail(Problems.Of(StatusCodes.Status400BadRequest, "invalid_frequency", "The frequency must be Weekly, Monthly or Quarterly."));
        }

        if (request.StartDate < clock.Today || request.EndDate < request.StartDate)
        {
            return Outcome.Fail(Problems.Of(StatusCodes.Status400BadRequest, "invalid_dates",
                "The start date must not be before the business date, and the end date not before the start date."));
        }

        var (from, _, problem) = await CashMovements.LoadAsync(db, http, request.FromAccountId, request.Amount, ct);
        if (problem is not null)
        {
            return Outcome.Fail(problem);
        }

        if (request.ToAccountId == request.FromAccountId)
        {
            return Outcome.Fail(Problems.Rule("same_account", "The source and target accounts are the same."));
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == request.ToAccountId, ct))
        {
            return Outcome.Fail(Problems.Rule("target_account_not_found", "The target account does not exist."));
        }

        var order = new StandingOrder
        {
            Id = Guid.CreateVersion7(),
            CustomerId = from!.CustomerId,
            FromAccountId = from.Id,
            ToAccountId = request.ToAccountId,
            Amount = request.Amount.Amount,
            Currency = request.Amount.Currency,
            Frequency = frequency,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = StandingOrderStatus.Active,
            CreatedAt = time.GetUtcNow(),
            PerformedBy = PerformedBy.From(performedBy),
        };
        order.NextRunDate = Logic.StandingOrders.FirstRunDate(order.Schedule);
        db.ChangeTracker.Clear(); // the source account was loaded tracked; nothing about it changes here
        db.StandingOrders.Add(order);
        return Outcome.Ok(StandingOrderResponse.From(order), StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpRequest http, AccountsDbContext db, CancellationToken ct)
    {
        var order = await db.StandingOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        return order is null || CustomerAccess.IsOtherCustomer(http, order.CustomerId)
            ? NotFound()
            : Results.Ok(StandingOrderResponse.From(order));
    }

    private static async Task<IResult> CancelAsync(
        Guid id, HttpRequest http, [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy, AccountsDbContext db, CancellationToken ct)
    {
        var order = await db.StandingOrders.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || CustomerAccess.IsOtherCustomer(http, order.CustomerId))
        {
            return NotFound();
        }

        if (order.Status == StandingOrderStatus.Active)
        {
            order.Status = StandingOrderStatus.Cancelled;
            order.NextRunDate = null;
            order.PerformedBy = PerformedBy.From(performedBy);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(StandingOrderResponse.From(order));
    }

    private static IResult NotFound() => Problems.NotFound("standing_order_not_found", "Standing order not found");
}
