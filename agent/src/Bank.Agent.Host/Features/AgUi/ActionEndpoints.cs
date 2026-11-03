using System.Text.Json;
using Bank.Agent.Host.Data;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.Host.Features.AgUi;

/// <summary>
/// What the person does outside the text box, each answering something the agent showed (phase 2 stage 3):
///
/// - POST /agent/changes/{id}/confirm and /cancel: the buttons of a confirmation card (flow 4.3).
/// - POST /agent/forms/{id}: the values of a form.
/// - POST /agent/documents/{id}: a file for a document request.
///
/// These are plain HTTP calls, not model calls: the model cannot press Confirm, send a form or upload a file. Each one
/// checks the session like a run, answers one pending item of the session once, and stores the outcome in the chat.
/// The chat UI then sends a short message ("Confirmed.") so the agent continues with the outcome in its history.
/// </summary>
public static class ActionEndpoints
{
    public const long MaxDocumentBytes = 5 * 1024 * 1024;

    public static readonly string[] DocumentTypes = ["application/pdf", "image/png", "image/jpeg"];

    public sealed record FormSubmission(Dictionary<string, string?> Values);

    /// <summary>The answer to the browser: done, failed or cancelled, and the bank's answer or the reason.</summary>
    public sealed record Outcome(string Status, string? Message);

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agent").WithTags("Agent");
        group.MapPost("/changes/{id}/confirm", ConfirmAsync).WithSummary("Run a change the person confirmed.");
        group.MapPost("/changes/{id}/cancel", CancelAsync).WithSummary("Drop a change the person cancelled.");
        group.MapPost("/forms/{id}", SubmitFormAsync).WithSummary("Send the values of a form.");
        group.MapPost("/documents/{id}", UploadAsync).DisableAntiforgery().WithSummary("Upload a document the agent asked for.");
    }

    private static async Task<IResult> ConfirmAsync(
        string id, HttpContext http, SessionGuard guard, ChatStore chats, IBankTools bankTools, ILogger<Outcome> log, CancellationToken ct)
    {
        var (session, chat, problem) = await OpenAsync(http, guard, chats, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (Take<PendingChange>(session!, id) is not { } change)
        {
            return NotPending();
        }

        var outcome = await RunAsync(session!, change.Id, change.Tool, Arguments(change.Arguments), bankTools, ct);
        log.LogInformation("Change {Tool} in chat {ChatId} for role {Role}: {Outcome}", change.Tool, session!.ChatId, session.Principal.Role, outcome.Status);
        await chats.AppendActionAsync(chat!, MessageKind.ChangeResult,
            Json(new { changeId = change.Id, title = change.Title, status = outcome.Status, bank = outcome.Message }), session.Principal.Username, ct);
        return TypedResults.Ok(outcome);
    }

    private static async Task<IResult> CancelAsync(string id, HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var (session, chat, problem) = await OpenAsync(http, guard, chats, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (Take<PendingChange>(session!, id) is not { } change)
        {
            return NotPending();
        }

        await chats.AppendActionAsync(chat!, MessageKind.ChangeResult,
            Json(new { changeId = change.Id, title = change.Title, status = "cancelled" }), session!.Principal.Username, ct);
        return TypedResults.Ok(new Outcome("cancelled", null));
    }

    private static async Task<IResult> SubmitFormAsync(
        string id, FormSubmission submission, HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var (session, chat, problem) = await OpenAsync(http, guard, chats, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!session!.Pending.TryGetValue(id, out var pending) || pending is not OpenForm form)
        {
            return NotPending();
        }

        // Wrong values keep the form open, so the person can correct them.
        var errors = FormCheck.Values(form.Fields, submission.Values);
        if (errors.Count > 0)
        {
            return TypedResults.BadRequest(new { code = "invalid_values", errors });
        }

        if (Take<OpenForm>(session, id) is null)
        {
            return NotPending();
        }

        var values = form.Fields.ToDictionary(f => f.Name, f => submission.Values.GetValueOrDefault(f.Name)?.Trim());
        await chats.AppendActionAsync(chat!, MessageKind.FormValues, Json(new { formId = form.Id, title = form.Title, values }), session.Principal.Username, ct);
        return TypedResults.Ok(new Outcome("done", null));
    }

    private static async Task<IResult> UploadAsync(
        string id, IFormFile? file, HttpContext http, SessionGuard guard, ChatStore chats, IBankTools bankTools, ILogger<Outcome> log, CancellationToken ct)
    {
        var (session, chat, problem) = await OpenAsync(http, guard, chats, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (file is null || file.Length == 0 || file.Length > MaxDocumentBytes || !DocumentTypes.Contains(file.ContentType))
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_document", "Upload one PDF, PNG or JPEG file of at most 5 MB.");
        }

        if (Take<DocumentRequest>(session!, id) is not { } request)
        {
            return NotPending();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var fileName = Path.GetFileName(file.FileName);
        var outcome = await RunAsync(session!, request.Id, "document_upload", new Dictionary<string, object?>
        {
            ["applicationId"] = request.ApplicationId,
            ["fileName"] = fileName,
            ["contentType"] = file.ContentType,
            ["contentBase64"] = Convert.ToBase64String(buffer.ToArray()),
        }, bankTools, ct);
        log.LogInformation("Document upload in chat {ChatId}: {Outcome}", session!.ChatId, outcome.Status);
        await chats.AppendActionAsync(chat!, MessageKind.Document,
            Json(new { requestId = request.Id, applicationId = request.ApplicationId, fileName, status = outcome.Status, bank = outcome.Message }),
            session.Principal.Username, ct);
        return TypedResults.Ok(outcome);
    }

    /// <summary>The session check of a run, plus: the chat still takes messages.</summary>
    private static async Task<(HostSession? Session, Chat? Chat, IResult? Problem)> OpenAsync(
        HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var guarded = await guard.CheckAsync(http, ct);
        if (guarded is GuardResult.Denied(var denied, _))
        {
            return (null, null, denied);
        }

        var session = ((GuardResult.Allowed)guarded).Session;
        var chat = await chats.FindAsync(session.ChatId, ct);
        return chat is null || !ChatAccess.CanWrite(session.Principal, new ChatFacts(chat.Username, chat.Status == ChatStatus.Open))
            ? (null, null, Problems.Of(StatusCodes.Status409Conflict, "chat_closed", "This chat no longer takes messages."))
            : (session, chat, null);
    }

    /// <summary>Takes a pending item of the right type out of the session: it can be answered once.</summary>
    private static T? Take<T>(HostSession session, string id) where T : PendingAction =>
        session.Pending.TryGetValue(id, out var pending) && pending is T typed && session.Pending.TryRemove(new(id, pending)) ? typed : null;

    /// <summary>Runs one bank tool with the confirmation's id as idempotency key and turns its answer into an outcome.</summary>
    private static async Task<Outcome> RunAsync(
        HostSession session, string confirmationId, string tool, IDictionary<string, object?> arguments, IBankTools bankTools, CancellationToken ct)
    {
        try
        {
            await using var bank = await bankTools.OpenForCustomerAsync(session, ct, confirmationId);
            var function = bank.Find(tool) ?? throw new InvalidOperationException($"The bank has no tool '{tool}'.");
            return ToolResult.ToOutcome(await function.InvokeAsync(new AIFunctionArguments(arguments), ct));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new Outcome("failed", $"The bank could not be reached ({ex.GetType().Name}). Nothing was changed, or check before trying again.");
        }
    }

    private static Dictionary<string, object?> Arguments(JsonElement json) =>
        json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

    private static string Json(object value) => JsonSerializer.Serialize(value, UiTools.Json);

    private static IResult NotPending() =>
        Problems.Of(StatusCodes.Status404NotFound, "not_pending", "This was already answered, or it belongs to a session that ended.");
}

/// <summary>What a bank tool answered, as done or failed with the bank's text (an MCP tool error is a failure).</summary>
public static class ToolResult
{
    public static ActionEndpoints.Outcome ToOutcome(object? result)
    {
        if (result is JsonElement { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("content", out var content))
        {
            var text = string.Concat(content.EnumerateArray().Where(c => c.TryGetProperty("text", out _)).Select(c => c.GetProperty("text").GetString()));
            var failed = json.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True;
            return new ActionEndpoints.Outcome(failed ? "failed" : "done", text);
        }

        return new ActionEndpoints.Outcome("done", result switch
        {
            null => null,
            string s => s,
            JsonElement e => e.GetRawText(),
            _ => JsonSerializer.Serialize(result),
        });
    }
}
