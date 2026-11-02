using System.Text.Json;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Applications;

public sealed record StageRequest(DateOnly DueDate, Money PrincipalShare);

public sealed record StatusChangeResponse(string? FromStatus, string ToStatus, string? Reason, DateOnly BusinessDate, string PerformedBy);

public sealed record ApplicationResponse(
    Guid Id, Guid CustomerId, string ProductCode, Money RequestedAmount, int TermMonths, Guid RepaymentAccountId,
    IReadOnlyList<StageRequest>? Stages, string Purpose, string Status, Guid? LoanId, IReadOnlyList<StatusChangeResponse> History)
{
    public static ApplicationResponse From(LoanApplication a) => new(
        a.Id, a.CustomerId, a.ProductCode, new Money(a.RequestedAmount, a.Currency), a.TermMonths, a.RepaymentAccountId,
        a.ProposedStages is null ? null : JsonSerializer.Deserialize<List<StageRequest>>(a.ProposedStages, ContractJson.Options),
        a.Purpose, a.Status.ToString(), a.LoanId,
        [.. a.History.OrderBy(h => h.CreatedAt).Select(h => new StatusChangeResponse(h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Reason, h.BusinessDate, h.PerformedBy))]);
}

/// <summary>
/// Loan applications: submit, read with history, and the review actions (review, request information, approve, reject,
/// cancel). The system never decides approval; review actions are calls by staff or, in phase 2, the agent.
/// </summary>
public static class ApplicationEndpoints
{
    public sealed record SubmitRequest(
        Guid CustomerId, string ProductCode, Money Amount, int TermMonths, Guid RepaymentAccountId, string Purpose, IReadOnlyList<StageRequest>? Stages);

    public sealed record ReasonRequest(string? Reason);

    public sealed record InformationRequest(IReadOnlyList<string> Items);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/loan-products", async (LendingDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct))).WithTags("Products");
        app.MapPost("/loan-applications", SubmitAsync).WithTags("Applications");
        app.MapGet("/loan-applications/{id:guid}", GetAsync).WithTags("Applications");
        app.MapPost("/loan-applications/{id:guid}/review", (Guid id, HttpRequest http, [AsParameters] Services s, CancellationToken ct) =>
            ActAsync(id, ApplicationAction.Review, null, staffOnly: true, http, s, ct)).WithTags("Applications");
        app.MapPost("/loan-applications/{id:guid}/approve", (Guid id, HttpRequest http, [AsParameters] Services s, CancellationToken ct) =>
            ActAsync(id, ApplicationAction.Approve, null, staffOnly: true, http, s, ct)).WithTags("Applications");
        app.MapPost("/loan-applications/{id:guid}/reject", (Guid id, ReasonRequest body, HttpRequest http, [AsParameters] Services s, CancellationToken ct) =>
            ActAsync(id, ApplicationAction.Reject, body.Reason, staffOnly: true, http, s, ct)).WithTags("Applications");
        app.MapPost("/loan-applications/{id:guid}/cancel", (Guid id, ReasonRequest body, HttpRequest http, [AsParameters] Services s, CancellationToken ct) =>
            ActAsync(id, ApplicationAction.Cancel, body.Reason, staffOnly: false, http, s, ct)).WithTags("Applications");
        app.MapPost("/loan-applications/{id:guid}/information-requests", RequestInformationAsync).WithTags("Applications");
    }

    /// <summary>What every application endpoint needs, as one injected parameter object.</summary>
    public sealed record Services(LendingDbContext Db, IEventPublisher Publisher, IBusinessClock Clock, TimeProvider Time);

    private static async Task<IResult> SubmitAsync(SubmitRequest request, HttpRequest http, [AsParameters] Services s, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, request.CustomerId))
        {
            return Problems.NotFound("customer_not_found", "Customer not found");
        }

        var product = await s.Db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Code == request.ProductCode, ct);
        if (product is null)
        {
            return Problems.Rule("product_not_found", $"No loan product '{request.ProductCode}'.");
        }

        if (!await s.Db.Customers.AnyAsync(c => c.CustomerId == request.CustomerId, ct))
        {
            return Problems.Rule("customer_not_found", "The customer is not known to Lending yet; try again in a moment.");
        }

        var account = await s.Db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountId == request.RepaymentAccountId, ct);
        if (account is null || account.CustomerId != request.CustomerId || account.Currency != product.Currency)
        {
            // Loan currency = repayment account currency (design plan section 2).
            return Problems.Rule("repayment_account_invalid", $"The repayment account must be the customer's own {product.Currency} account.");
        }

        if (request.Amount is null || request.Amount.Currency != product.Currency
            || request.Amount.Amount < product.MinAmount || request.Amount.Amount > product.MaxAmount)
        {
            return Problems.Rule("amount_out_of_range", $"{product.Code} lends {product.MinAmount}–{product.MaxAmount} {product.Currency}.");
        }

        if (request.TermMonths < product.MinTermMonths || request.TermMonths > product.MaxTermMonths)
        {
            return Problems.Rule("term_out_of_range", $"{product.Code} runs {product.MinTermMonths}–{product.MaxTermMonths} months.");
        }

        if (string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 500)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_purpose", "A purpose of at most 500 characters is required.");
        }

        var minorUnits = await s.Db.Currencies.Where(c => c.Code == product.Currency).Select(c => c.MinorUnits).SingleOrDefaultAsync(ct);
        if (StageProblem(product, request, s.Clock.Today, minorUnits) is { } stageProblem)
        {
            return stageProblem;
        }

        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        var now = s.Time.GetUtcNow();
        var application = new LoanApplication
        {
            Id = Guid.CreateVersion7(), CustomerId = request.CustomerId, ProductCode = product.Code, RequestedAmount = request.Amount.Amount,
            Currency = product.Currency, TermMonths = request.TermMonths, RepaymentAccountId = account.AccountId,
            ProposedStages = request.Stages is null ? null : JsonSerializer.Serialize(request.Stages, ContractJson.Options),
            Purpose = request.Purpose.Trim(), Status = ApplicationStatus.Submitted, CreatedAt = now, PerformedBy = actor,
        };
        application.History.Add(new ApplicationStatusChange
        {
            Id = Guid.CreateVersion7(), ApplicationId = application.Id, FromStatus = null, ToStatus = ApplicationStatus.Submitted,
            BusinessDate = s.Clock.Today, CreatedAt = now, PerformedBy = actor,
        });
        s.Db.Applications.Add(application);
        await s.Publisher.PublishAsync(
            new LoanApplicationSubmitted(Guid.NewGuid(), s.Clock.Today, actor, application.Id, application.CustomerId, product.Code,
                request.Amount, request.TermMonths),
            application.Id.ToString(), ct);
        await s.Db.SaveChangesAsync(ct);
        return Results.Created($"/loan-applications/{application.Id}", ApplicationResponse.From(application));
    }

    /// <summary>Staged products need a valid plan (checked with the stage 3 logic, against today as a provisional disbursement date).</summary>
    internal static IResult? StageProblem(LoanProduct product, SubmitRequest request, DateOnly today, int minorUnits)
    {
        if (product.RepaymentType == RepaymentType.Annuity)
        {
            return request.Stages is { Count: > 0 } ? Problems.Rule("stages_not_allowed", "Annuity products have no stage plan.") : null;
        }

        if (request.Stages is not { Count: > 0 })
        {
            return Problems.Rule("stage_plan_required", "Staged products need a stage plan.");
        }

        var result = Amortization.BuildStaged(Terms(product, request.Amount, request.TermMonths, today, minorUnits),
            new StagePlan([.. request.Stages.Select(st => new Stage(st.DueDate, st.PrincipalShare))]));
        return result.IsSuccess ? null : Problems.Rule(result.Error.Code, result.Error.Message);
    }

    internal static LoanTerms Terms(LoanProduct p, Money principal, int termMonths, DateOnly disbursedOn, int minorUnits) => new(
        principal, p.AnnualRate, termMonths, p.RepaymentType, p.PenaltyDailyRate, p.GraceDays, p.OverdueAfterDays, p.DefaultAfterDays,
        p.PayoffMinPercent, new Money(p.PayoffCap, p.Currency), disbursedOn, minorUnits, p.MaxStages);

    private static async Task<IResult> GetAsync(Guid id, HttpRequest http, LendingDbContext db, CancellationToken ct)
    {
        var application = await db.Applications.AsNoTracking().Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, ct);
        return application is null || CustomerAccess.IsOtherCustomer(http, application.CustomerId)
            ? NotFound()
            : Results.Ok(ApplicationResponse.From(application));
    }

    private static async Task<IResult> ActAsync(
        Guid id, ApplicationAction action, string? reason, bool staffOnly, HttpRequest http, Services s, CancellationToken ct)
    {
        if (staffOnly && CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var application = await s.Db.Applications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (application is null || CustomerAccess.IsOtherCustomer(http, application.CustomerId))
        {
            return NotFound();
        }

        var result = await Transitions.ApplyAsync(application, action, reason?.Trim(), PerformedBy.From(http.Headers[PerformedBy.HeaderName]),
            s.Clock.Today, s.Time.GetUtcNow(), s.Publisher, ct);
        if (!result.IsSuccess)
        {
            return Transitions.Conflict(result.Error);
        }

        await s.Db.SaveChangesAsync(ct);
        return Results.Ok(ApplicationResponse.From(application));
    }

    private static async Task<IResult> RequestInformationAsync(
        Guid id, InformationRequest body, HttpRequest http, [AsParameters] Services s, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var items = body.Items?.Select(i => i?.Trim()).Where(i => !string.IsNullOrEmpty(i)).ToList() ?? [];
        if (items.Count == 0)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "no_items", "List at least one requested item.");
        }

        var application = await s.Db.Applications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return NotFound();
        }

        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        var result = await Transitions.ApplyAsync(application, ApplicationAction.RequestInformation, null, actor, s.Clock.Today, s.Time.GetUtcNow(), s.Publisher, ct);
        if (!result.IsSuccess)
        {
            return Transitions.Conflict(result.Error);
        }

        await s.Publisher.PublishAsync(
            new InformationRequested(Guid.NewGuid(), s.Clock.Today, actor, Guid.CreateVersion7(), application.Id, application.CustomerId, string.Join('\n', items)),
            application.Id.ToString(), ct);
        await s.Db.SaveChangesAsync(ct);
        return Results.Ok(ApplicationResponse.From(application));
    }

    internal static IResult NotFound() => Problems.NotFound("application_not_found", "Loan application not found");
}
