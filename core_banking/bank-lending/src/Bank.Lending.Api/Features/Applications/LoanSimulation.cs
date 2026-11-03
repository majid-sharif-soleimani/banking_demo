using Bank.Common;
using Bank.Contracts;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Applications;

public sealed record SimulatedItem(int Number, DateOnly DueDate, Money Principal, Money Interest, Money Total);

/// <param name="DisbursedOn">The assumed disbursement date: today's business date. A real loan counts from its own disbursement.</param>
/// <param name="Installment">The monthly annuity amount; null for staged products, whose stages differ (see the schedule).</param>
public sealed record SimulationResponse(
    string ProductCode, Money Amount, int TermMonths, decimal AnnualRate, DateOnly DisbursedOn,
    Money? Installment, Money TotalInterest, Money TotalCost, IReadOnlyList<SimulatedItem> Schedule);

/// <summary>
/// POST /loan-products/{code}/simulation: what a loan of this product would cost, built with the same product limits and
/// the same Amortization logic as a real disbursement on today's business date. It stores nothing and publishes nothing,
/// so customers and staff may both call it. POST because a staged plan is a list, which does not fit a query string.
/// </summary>
public static class LoanSimulation
{
    public sealed record SimulationRequest(Money Amount, int TermMonths, IReadOnlyList<StageRequest>? Stages);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/loan-products/{code}/simulation", SimulateAsync)
            .WithName("SimulateLoan").WithTags("Products")
            .WithSummary("Simulate a loan: installment, total interest, total cost and the full schedule. Stores nothing.")
            .Produces<SimulationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> SimulateAsync(
        string code, SimulationRequest request, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Code == code, ct);
        if (product is null)
        {
            return Problems.NotFound("product_not_found", $"No loan product '{code}'.");
        }

        if (ApplicationEndpoints.LimitProblem(product, request.Amount, request.TermMonths) is { } limitProblem)
        {
            return limitProblem;
        }

        if (product.RepaymentType == RepaymentType.Annuity ? request.Stages is { Count: > 0 } : request.Stages is not { Count: > 0 })
        {
            return product.RepaymentType == RepaymentType.Annuity
                ? Problems.Rule("stages_not_allowed", "Annuity products have no stage plan.")
                : Problems.Rule("stage_plan_required", "Staged products need a stage plan.");
        }

        var today = clock.Today;
        var minorUnits = await db.Currencies.Where(c => c.Code == product.Currency).Select(c => c.MinorUnits).SingleAsync(ct);
        var terms = ApplicationEndpoints.Terms(product, request.Amount, request.TermMonths, today, minorUnits);
        var schedule = product.RepaymentType == RepaymentType.Annuity
            ? Amortization.BuildAnnuity(terms)
            : Amortization.BuildStaged(terms, ApplicationEndpoints.ToPlan(request.Stages!));
        if (!schedule.IsSuccess)
        {
            return Problems.Rule(schedule.Error.Code, schedule.Error.Message);
        }

        var cost = LoanCost.Summarize(schedule.Value, product.RepaymentType);
        return Results.Ok(new SimulationResponse(
            product.Code, request.Amount, request.TermMonths, product.AnnualRate, today, cost.Installment, cost.TotalInterest, cost.TotalCost,
            [.. schedule.Value.Select(i => new SimulatedItem(i.Number, i.DueDate, i.PrincipalDue, i.InterestDue, i.PrincipalDue + i.InterestDue))]));
    }
}
