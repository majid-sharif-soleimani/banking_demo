using System.Text.Json;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Lending.Api.Features.Loans;

/// <summary>
/// POST /loan-applications/{id}/disburse: Approved → Disbursed. Copies the product's parameters onto a new loan
/// (design plan section 2), builds the schedule with the stage 3 logic, and publishes LoanDisbursed: Accounts credits the
/// repayment account, Ledger posts the loan. A second disburse is 409 (state machine, and a unique loan per application).
/// </summary>
public static class Disbursement
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/loan-applications/{id:guid}/disburse", DisburseAsync).WithTags("Applications");

    private static async Task<IResult> DisburseAsync(
        Guid id, HttpRequest http, [AsParameters] ApplicationEndpoints.Services s, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var application = await s.Db.Applications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return ApplicationEndpoints.NotFound();
        }

        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        var today = s.Clock.Today;
        var now = s.Time.GetUtcNow();
        var transition = await Transitions.ApplyAsync(application, ApplicationAction.Disburse, null, actor, today, now, s.Publisher, ct);
        if (!transition.IsSuccess)
        {
            return Transitions.Conflict(transition.Error);
        }

        var product = await s.Db.Products.AsNoTracking().SingleAsync(p => p.Code == application.ProductCode, ct);
        var minorUnits = await s.Db.Currencies.Where(c => c.Code == application.Currency).Select(c => c.MinorUnits).SingleAsync(ct);
        var principal = new Money(application.RequestedAmount, application.Currency);
        var terms = ApplicationEndpoints.Terms(product, principal, application.TermMonths, today, minorUnits);
        var schedule = product.RepaymentType == RepaymentType.Annuity
            ? Amortization.BuildAnnuity(terms)
            : Amortization.BuildStaged(terms, new StagePlan([.. JsonSerializer.Deserialize<List<StageRequest>>(application.ProposedStages!, ContractJson.Options)!
                .Select(st => new Stage(st.DueDate, st.PrincipalShare))]));
        if (!schedule.IsSuccess)
        {
            // A stage plan checked at submission can fail now, for example a stage date that has meanwhile passed.
            return Problems.Rule(schedule.Error.Code, schedule.Error.Message);
        }

        var loan = new Loan
        {
            Id = Guid.CreateVersion7(), ApplicationId = application.Id, CustomerId = application.CustomerId,
            RepaymentAccountId = application.RepaymentAccountId, ProductCode = product.Code, Principal = principal.Amount,
            Currency = principal.Currency, MinorUnits = minorUnits, RepaymentType = product.RepaymentType, AnnualRate = product.AnnualRate,
            TermMonths = application.TermMonths, PenaltyDailyRate = product.PenaltyDailyRate, GraceDays = product.GraceDays,
            OverdueAfterDays = product.OverdueAfterDays, DefaultAfterDays = product.DefaultAfterDays, PayoffMinPercent = product.PayoffMinPercent,
            PayoffCap = product.PayoffCap, MaxStages = product.MaxStages, DisbursedOn = today, Status = LoanStatus.Active,
            Classification = LoanClassification.Current, CreatedAt = now, PerformedBy = actor,
        };
        foreach (var item in schedule.Value)
        {
            var row = new ScheduleItemRow { LoanId = loan.Id, Number = item.Number, DueDate = item.DueDate, CreatedAt = now, PerformedBy = actor };
            row.Apply(item);
            loan.Schedule.Add(row);
        }

        s.Db.Loans.Add(loan);
        application.LoanId = loan.Id;
        await s.Publisher.PublishAsync(
            new LoanDisbursed(Guid.NewGuid(), today, actor, loan.Id, application.Id, loan.CustomerId, loan.RepaymentAccountId, principal),
            loan.Id.ToString(), ct);

        try
        {
            await s.Db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problems.Of(StatusCodes.Status409Conflict, "invalid_transition", "The application was disbursed meanwhile.");
        }

        return Results.Created($"/loans/{loan.Id}", LoanResponse.From(loan, today));
    }
}

/// <summary>GET /loans?customerId= and GET /loans/{id}: the loan with its schedule and its current state.</summary>
public static class LoanQueries
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/loans", async (Guid customerId, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct) =>
        {
            if (CustomerAccess.IsOtherCustomer(http, customerId))
            {
                return Results.Ok(Array.Empty<LoanResponse>());
            }

            var loans = await db.Loans.AsNoTracking().Include(l => l.Schedule).Where(l => l.CustomerId == customerId).OrderBy(l => l.CreatedAt).ToListAsync(ct);
            return Results.Ok(loans.Select(l => LoanResponse.From(l, clock.Today)));
        }).WithTags("Loans");

        app.MapGet("/loans/{id:guid}", async (Guid id, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct) =>
        {
            var loan = await db.Loans.AsNoTracking().Include(l => l.Schedule).SingleOrDefaultAsync(l => l.Id == id, ct);
            return loan is null || CustomerAccess.IsOtherCustomer(http, loan.CustomerId)
                ? NotFound()
                : Results.Ok(LoanResponse.From(loan, clock.Today));
        }).WithTags("Loans");
    }

    public static IResult NotFound() => Problems.NotFound("loan_not_found", "Loan not found");
}
