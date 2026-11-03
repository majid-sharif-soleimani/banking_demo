using Bank.Common;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Customers.Api.Features.Appointments;

public sealed record SlotResponse(string EmployeeId, string EmployeeName, DateTimeOffset StartsAt);

public sealed record AppointmentResponse(Guid Id, Guid ApplicationId, Guid CustomerId, string EmployeeId, DateTimeOffset StartsAt, string Status);

/// <summary>
/// In-person appointments during loan review (design plan section 2): free slots per employee on a day after the current
/// business date, booking (one scheduled appointment per employee and slot, enforced by a unique index; publishes
/// AppointmentScheduled for Lending), and cancelling.
/// </summary>
public static class AppointmentEndpoints
{
    public sealed record BookRequest(Guid ApplicationId, Guid CustomerId, string EmployeeId, DateTimeOffset StartsAt);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments/availability", AvailabilityAsync).WithTags("Appointments");
        app.MapGet("/appointments", ListAsync).WithTags("Appointments");
        app.MapPost("/appointments", BookAsync).WithTags("Appointments");
        app.MapDelete("/appointments/{id:guid}", CancelAsync).WithTags("Appointments");
    }

    private static async Task<IResult> AvailabilityAsync(DateOnly date, CustomersDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (date <= clock.Today)
        {
            return Bad("invalid_date", "Appointments are for a day after the current business date.");
        }

        var slots = AppointmentSlots.For(date);
        // Npgsql writes only UTC offsets to timestamptz.
        var (first, last) = (slots[0].ToUniversalTime(), slots[^1].ToUniversalTime());
        var taken = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Scheduled && a.StartsAt >= first && a.StartsAt <= last)
            .Select(a => new { a.EmployeeId, a.StartsAt })
            .ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);
        return Results.Ok(
            from slot in slots
            from employee in employees
            where !taken.Any(t => t.EmployeeId == employee.Id && t.StartsAt == slot)
            select new SlotResponse(employee.Id, employee.Name, slot));
    }

    /// <summary>
    /// GET /appointments?customerId=: a customer's appointments, soonest first, scheduled and cancelled (phase 2 design
    /// plan, section 8). Another customer's id gives an empty list, like the other per-customer lists.
    /// </summary>
    private static async Task<IResult> ListAsync(Guid customerId, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, customerId))
        {
            return Results.Ok(Array.Empty<AppointmentResponse>());
        }

        var appointments = await db.Appointments.AsNoTracking().Where(a => a.CustomerId == customerId).OrderBy(a => a.StartsAt).ToListAsync(ct);
        return Results.Ok(appointments.Select(ToResponse));
    }

    private static async Task<IResult> BookAsync(
        BookRequest request, HttpRequest http, CustomersDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, request.CustomerId) || !await db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
        {
            return GetCustomer.NotFound();
        }

        if (!await db.Applications.AnyAsync(a => a.ApplicationId == request.ApplicationId && a.CustomerId == request.CustomerId, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Loan application not found for this customer",
                extensions: new Dictionary<string, object?> { ["code"] = "application_not_found" });
        }

        if (!AppointmentSlots.IsSlot(request.StartsAt) || AppointmentSlots.LocalDate(request.StartsAt) <= clock.Today)
        {
            return Bad("invalid_slot", "Book a full hour from 09:00 to 15:00 Stockholm time, on a day after the current business date.");
        }

        if (!await db.Employees.AnyAsync(e => e.Id == request.EmployeeId, ct))
        {
            return Bad("unknown_employee", $"No employee '{request.EmployeeId}'.");
        }

        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(), ApplicationId = request.ApplicationId, CustomerId = request.CustomerId, EmployeeId = request.EmployeeId,
            StartsAt = request.StartsAt.ToUniversalTime(), Status = AppointmentStatus.Scheduled, CreatedAt = time.GetUtcNow(), PerformedBy = actor,
        };
        db.Appointments.Add(appointment);
        await publisher.PublishAsync(
            new AppointmentScheduled(Guid.NewGuid(), clock.Today, actor, appointment.Id, appointment.ApplicationId, appointment.EmployeeId, appointment.StartsAt),
            appointment.ApplicationId.ToString(), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CustomersDbContext.AppointmentSlotIndex })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The employee already has an appointment at that time",
                extensions: new Dictionary<string, object?> { ["code"] = "slot_taken" });
        }

        return Results.Created($"/appointments/{appointment.Id}", ToResponse(appointment));
    }

    private static async Task<IResult> CancelAsync(Guid id, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null || CustomerAccess.IsOtherCustomer(http, appointment.CustomerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Appointment not found",
                extensions: new Dictionary<string, object?> { ["code"] = "appointment_not_found" });
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.PerformedBy = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(appointment));
    }

    private static AppointmentResponse ToResponse(Appointment a) =>
        new(a.Id, a.ApplicationId, a.CustomerId, a.EmployeeId, a.StartsAt, a.Status.ToString());

    private static IResult Bad(string code, string title) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
