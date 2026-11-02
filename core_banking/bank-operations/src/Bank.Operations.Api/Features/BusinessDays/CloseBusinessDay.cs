using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Operations.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Operations.Api.Features.BusinessDays;

/// <summary>
/// POST /business-days/current/close: Open → Closing and DayClosed, which starts the participants' end-of-day work
/// (design plan ADR 15). Only an Open day can be closed; anything else is 409, so DayClosed is published once per day.
/// </summary>
public static class CloseBusinessDay
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/business-days/current/close", HandleAsync).WithName(nameof(CloseBusinessDay)).WithTags("Business days");

    private static async Task<Results<Ok<BusinessDayResponse>, ProblemHttpResult>> HandleAsync(
        [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        OperationsDbContext db,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var day = await db.BusinessDays.CurrentAsync(ct);
        if (day is null || day.Status != BusinessDayStatus.Open)
        {
            return NotOpen(day);
        }

        var actor = PerformedBy.From(performedBy);
        day.Status = BusinessDayStatus.Closing;
        day.PerformedBy = actor;
        await publisher.PublishAsync(new DayClosed(Guid.NewGuid(), day.Date, actor, day.Date), DayKey(day.Date), ct);

        try
        {
            // The update carries the row's xmin: of two concurrent closes, only one changes the row and publishes.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotOpen(day);
        }

        return TypedResults.Ok(BusinessDayResponse.From(day));
    }

    /// <summary>The Kafka key of every business day event: the date, so the events of one day stay in order.</summary>
    internal static string DayKey(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static ProblemHttpResult NotOpen(BusinessDay? day) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The business day is not open",
            detail: day is null ? "No business day is open yet." : $"Business day {day.Date:yyyy-MM-dd} is already closing.",
            extensions: new Dictionary<string, object?> { ["code"] = "business_day_not_open" });
}
