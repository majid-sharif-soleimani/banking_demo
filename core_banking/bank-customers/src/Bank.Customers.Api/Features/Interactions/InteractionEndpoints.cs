using Bank.Common;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Features.Customers;
using Bank.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Interactions;

public sealed record InteractionResponse(
    Guid Id, Guid? ApplicationId, string Kind, string? FileName, string? ContentType, long? FileSize, string? Content, DateOnly ReceivedOn);

public sealed record InformationRequestResponse(Guid Id, Guid ApplicationId, Guid CustomerId, IReadOnlyList<string> Items, string Status, DateOnly RequestedOn);

/// <summary>
/// The customer interaction store (design plan section 2): uploads (files in IFileStorage), email replies and notes,
/// optionally linked to a loan application; and the information requests whose completion tells Lending the
/// information arrived (InformationReceived).
/// </summary>
public static class InteractionEndpoints
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/customers/{id:guid}/interactions", AddAsync).DisableAntiforgery().WithTags("Interactions");
        app.MapGet("/customers/{id:guid}/interactions", ListAsync).WithTags("Interactions");
        app.MapGet("/customers/{id:guid}/interactions/{interactionId:guid}/file", FileAsync).WithTags("Interactions");
        app.MapGet("/information-requests", RequestsAsync).WithTags("Information requests");
        app.MapPost("/information-requests/{id:guid}/complete", CompleteAsync).WithTags("Information requests");
    }

    private static async Task<IResult> AddAsync(
        Guid id, [FromForm] string kind, [FromForm] Guid? applicationId, [FromForm] string? content, IFormFile? file,
        HttpRequest http, CustomersDbContext db, IFileStorage files, IBusinessClock clock, TimeProvider time, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, id) || !await db.Customers.AnyAsync(c => c.Id == id, ct))
        {
            return GetCustomer.NotFound();
        }

        if (!Enum.TryParse<InteractionKind>(kind, ignoreCase: false, out var interactionKind) || !Enum.IsDefined(interactionKind))
        {
            return Problem("invalid_kind", "The kind must be Upload, EmailReply or Note.");
        }

        if (interactionKind == InteractionKind.Upload ? file is null || file.Length == 0 : string.IsNullOrWhiteSpace(content))
        {
            return Problem("missing_content", interactionKind == InteractionKind.Upload ? "An upload needs a file." : "The text is required.");
        }

        if (applicationId is { } application && !await db.Applications.AnyAsync(a => a.ApplicationId == application && a.CustomerId == id, ct))
        {
            return Problem("application_not_found", "The loan application does not belong to this customer.");
        }

        if (file is { Length: > MaxFileBytes })
        {
            return Problem("file_too_large", "Files may be at most 10 MB.");
        }

        var interactionId = Guid.CreateVersion7();
        string? key = null;
        if (interactionKind == InteractionKind.Upload)
        {
            // The stored name is ours; the customer's file name is kept only as metadata, never as a path.
            key = $"customers/{id}/{interactionId}";
            await using var stream = file!.OpenReadStream();
            await files.SaveAsync(key, stream, ct);
        }

        var interaction = new Interaction
        {
            Id = interactionId, CustomerId = id, ApplicationId = applicationId, Kind = interactionKind, FileKey = key,
            FileName = file is null ? null : Path.GetFileName(file.FileName), ContentType = file?.ContentType, FileSize = file?.Length,
            Content = content?.Trim(), ReceivedOn = clock.Today, CreatedAt = time.GetUtcNow(), PerformedBy = PerformedBy.From(http.Headers[PerformedBy.HeaderName]),
        };
        db.Interactions.Add(interaction);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch when (key is not null)
        {
            // No orphan file when the row cannot be saved.
            await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }
        return Results.Created($"/customers/{id}/interactions/{interactionId}", ToResponse(interaction));
    }

    private static async Task<IResult> ListAsync(Guid id, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, id))
        {
            return GetCustomer.NotFound();
        }

        var interactions = await db.Interactions.AsNoTracking().Where(i => i.CustomerId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return Results.Ok(interactions.Select(ToResponse));
    }

    private static async Task<IResult> FileAsync(Guid id, Guid interactionId, HttpRequest http, CustomersDbContext db, IFileStorage files, CancellationToken ct)
    {
        var interaction = await db.Interactions.AsNoTracking().SingleOrDefaultAsync(i => i.Id == interactionId && i.CustomerId == id, ct);
        if (interaction?.FileKey is null || CustomerAccess.IsOtherCustomer(http, id))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "File not found",
                extensions: new Dictionary<string, object?> { ["code"] = "file_not_found" });
        }

        return Results.Stream(await files.OpenReadAsync(interaction.FileKey, ct), interaction.ContentType ?? "application/octet-stream", interaction.FileName);
    }

    private static async Task<IResult> RequestsAsync(Guid applicationId, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        var requests = await db.InformationRequests.AsNoTracking().Where(r => r.ApplicationId == applicationId).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        return Results.Ok(requests.Where(r => !CustomerAccess.IsOtherCustomer(http, r.CustomerId)).Select(ToResponse));
    }

    private static async Task<IResult> CompleteAsync(Guid id, HttpRequest http, CustomersDbContext db, IEventPublisher publisher, IBusinessClock clock, CancellationToken ct)
    {
        var request = await db.InformationRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || CustomerAccess.IsOtherCustomer(http, request.CustomerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Information request not found",
                extensions: new Dictionary<string, object?> { ["code"] = "information_request_not_found" });
        }

        if (request.Status == InformationRequestStatus.Open)
        {
            var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
            request.Status = InformationRequestStatus.Completed;
            request.PerformedBy = actor;
            await publisher.PublishAsync(new InformationReceived(Guid.NewGuid(), clock.Today, actor, request.Id, request.ApplicationId),
                request.ApplicationId.ToString(), ct);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(ToResponse(request));
    }

    private static InteractionResponse ToResponse(Interaction i) =>
        new(i.Id, i.ApplicationId, i.Kind.ToString(), i.FileName, i.ContentType, i.FileSize, i.Content, i.ReceivedOn);

    private static InformationRequestResponse ToResponse(InformationRequest r) =>
        new(r.Id, r.ApplicationId, r.CustomerId, r.RequestedItems.Split('\n'), r.Status.ToString(), r.RequestedOn);

    private static IResult Problem(string code, string title) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
