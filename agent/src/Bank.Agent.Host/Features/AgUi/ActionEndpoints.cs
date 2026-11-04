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

    /// <summary>
    /// The answer to the browser: done, failed (the bank refused), cancelled, or unknown (the bank did not answer, so the
    /// change may or may not have happened), and the bank's answer or the reason.
    /// </summary>
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
        var opened = await OpenAsync(http, guard, chats, ct);
        if (opened is not Opened.Ok(var session, var chat))
        {
            return Refused(opened);
        }

        if (Take<PendingChange>(session, id) is not { } change)
        {
            return NotPending();
        }

        var outcome = await RunAsync(session, change.Id, change.Tool, Arguments(change.Arguments), bankTools, ct);
        log.LogInformation("Change {Tool} in chat {ChatId} for role {Role}: {Outcome}", change.Tool, session.ChatId, session.Principal.Role, outcome.Status);
        if (outcome.Status == Unknown)
        {
            // The bank may have made the change. Keep it, so Confirm can be pressed again with the same idempotency key:
            // the bank then makes it at most once.
            session.Pending[change.Id] = change;
        }

        await RecordAsync(session, chat, MessageKind.ChangeResult,
            new { changeId = change.Id, title = change.Title, status = outcome.Status, bank = outcome.Message }, chats, ct);
        return TypedResults.Ok(outcome);
    }

    private static async Task<IResult> CancelAsync(string id, HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var opened = await OpenAsync(http, guard, chats, ct);
        if (opened is not Opened.Ok(var session, var chat))
        {
            return Refused(opened);
        }

        if (Take<PendingChange>(session, id) is not { } change)
        {
            return NotPending();
        }

        await RecordAsync(session, chat, MessageKind.ChangeResult, new { changeId = change.Id, title = change.Title, status = "cancelled" }, chats, ct);
        return TypedResults.Ok(new Outcome("cancelled", null));
    }

    private static async Task<IResult> SubmitFormAsync(
        string id, FormSubmission submission, HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var opened = await OpenAsync(http, guard, chats, ct);
        if (opened is not Opened.Ok(var session, var chat))
        {
            return Refused(opened);
        }

        if (!session.Pending.TryGetValue(id, out var pending) || pending is not OpenForm form)
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
        var numbers = FormCheck.Numbers(form.Fields, values);
        if (numbers.Count > 0)
        {
            session.EnteredNumbers = numbers;
        }

        await RecordAsync(session, chat, MessageKind.FormValues, new { formId = form.Id, title = form.Title, values }, chats, ct);
        return TypedResults.Ok(new Outcome("done", null));
    }

    private static async Task<IResult> UploadAsync(
        string id, IFormFile? file, HttpContext http, SessionGuard guard, ChatStore chats, IBankTools bankTools, ILogger<Outcome> log, CancellationToken ct)
    {
        var opened = await OpenAsync(http, guard, chats, ct);
        if (opened is not Opened.Ok(var session, var chat))
        {
            return Refused(opened);
        }

        if (file is null || file.Length == 0 || file.Length > MaxDocumentBytes || !DocumentTypes.Contains(file.ContentType))
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_document", "Upload one PDF, PNG or JPEG file of at most 5 MB.");
        }

        if (Take<DocumentRequest>(session, id) is not { } request)
        {
            return NotPending();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var fileName = Path.GetFileName(file.FileName);
        var outcome = await RunAsync(session, request.Id, McpBankTools.DocumentUpload, new Dictionary<string, object?>
        {
            ["applicationId"] = request.ApplicationId,
            ["fileName"] = fileName,
            ["contentType"] = file.ContentType,
            ["contentBase64"] = Convert.ToBase64String(buffer.ToArray()),
        }, bankTools, ct);
        log.LogInformation("Document upload in chat {ChatId}: {Outcome}", session.ChatId, outcome.Status);
        if (outcome.Status == Unknown)
        {
            // As for a change: the same request id is the idempotency key, so uploading again stores the file once.
            session.Pending[request.Id] = request;
        }

        await RecordAsync(session, chat, MessageKind.Document,
            new { requestId = request.Id, applicationId = request.ApplicationId, fileName, status = outcome.Status, bank = outcome.Message }, chats, ct);
        return TypedResults.Ok(outcome);
    }

    private const string Unknown = "unknown";

    /// <summary>The session check of a run, plus: the chat still takes messages.</summary>
    private abstract record Opened
    {
        public sealed record Ok(HostSession Session, Chat Chat) : Opened;

        public sealed record Denied(IResult Problem) : Opened;
    }

    private static async Task<Opened> OpenAsync(HttpContext http, SessionGuard guard, ChatStore chats, CancellationToken ct)
    {
        var guarded = await guard.CheckAsync(http, ct);
        if (guarded is not GuardResult.Allowed(var session))
        {
            return new Opened.Denied(((GuardResult.Denied)guarded).Problem);
        }

        // Only the owner and the status are needed here, not the messages.
        var chat = await chats.FindHeaderAsync(session.ChatId, ct);
        return chat is null || !ChatAccess.CanWrite(session.Principal, new ChatFacts(chat.Username, chat.Status == ChatStatus.Open))
            ? new Opened.Denied(Problems.Of(StatusCodes.Status409Conflict, "chat_closed", "This chat no longer takes messages."))
            : new Opened.Ok(session, chat);
    }

    private static IResult Refused(Opened opened) => ((Opened.Denied)opened).Problem;

    /// <summary>
    /// Stores the person's action in the chat, while holding the session's chat lock: a run that is still writing its
    /// answer finishes first, so the two never race for the next sequence number. The action has already happened at
    /// the bank, so this waits instead of refusing.
    /// </summary>
    private static async Task RecordAsync(HostSession session, Chat chat, string kind, object content, ChatStore chats, CancellationToken ct)
    {
        await session.ChatLock.WaitAsync(ct);
        try
        {
            await chats.AppendAsync(chat, "user", kind, JsonSerializer.Serialize(content, HostJson.Options), session.Principal.Username, ct);
        }
        finally
        {
            session.ChatLock.Release();
        }
    }

    /// <summary>Takes a pending item of the right type out of the session: it can be answered once.</summary>
    private static T? Take<T>(HostSession session, string id) where T : PendingAction =>
        session.Pending.TryGetValue(id, out var pending) && pending is T typed && session.Pending.TryRemove(new(id, pending)) ? typed : null;

    /// <summary>
    /// Runs one bank tool with the confirmation's id as idempotency key. A refusal by the bank is final ("failed"). No
    /// answer at all (timeout, broken connection) is "unknown": the bank may have done it, so the caller keeps the item.
    /// </summary>
    private static async Task<Outcome> RunAsync(
        HostSession session, string confirmationId, string tool, IDictionary<string, object?> arguments, IBankTools bankTools, CancellationToken ct)
    {
        try
        {
            await using var bank = await bankTools.OpenAsync(session, ct, confirmationId);
            var function = bank.Find(tool) ?? throw new InvalidOperationException($"The bank has no tool '{tool}'.");
            var answer = ToolResult.Read(await function.InvokeAsync(new AIFunctionArguments(arguments), ct));
            return new Outcome(answer.Done ? "done" : "failed", answer.Text);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new Outcome(Unknown, $"The bank did not answer ({ex.GetType().Name}). Press the button again: the bank makes the change only once.");
        }
    }

    private static Dictionary<string, object?> Arguments(JsonElement json) =>
        json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

    private static IResult NotPending() =>
        Problems.Of(StatusCodes.Status404NotFound, "not_pending", "This was already answered, or it belongs to a session that ended.");
}
