using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bank.Accounts.Api.Data;
using Bank.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Accounts.Api.Features.Movements;

/// <summary>What a money command produced: a success to store and return, or a problem (not stored, so a retry re-evaluates).</summary>
public sealed record Outcome(int StatusCode, object? Body, IResult? Problem)
{
    public static Outcome Ok(object body, int statusCode = StatusCodes.Status200OK) => new(statusCode, body, null);

    public static Outcome Fail(IResult problem) => new(0, null, problem);
}

/// <summary>
/// Runs a money-moving command with the bank's two protections:
/// <list type="bullet">
/// <item>Idempotency-Key (ADR 0003): required; the first success is stored with its response in the same transaction as
/// the effect, and the same key replays it. The same key with another body is 422 idempotency_key_reused.</item>
/// <item>Optimistic concurrency: when another request changed the account first (xmin), the whole command runs once
/// more on fresh data; a second conflict is 409 (stage 4 file, "Concurrency").</item>
/// </list>
/// </summary>
public static class IdempotentCommand
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> RunAsync(
        HttpRequest http, AccountsDbContext db, string operation, object request, Func<CancellationToken, Task<Outcome>> command, CancellationToken ct)
    {
        var key = http.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "idempotency_key_required",
                "Every request that moves money needs an Idempotency-Key header (at most 100 characters).");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));
        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);

        for (var attempt = 1; ; attempt++)
        {
            if (await ReplayAsync(db, operation, key, hash, ct) is { } replay)
            {
                return replay;
            }

            var outcome = await command(ct);
            if (outcome.Problem is not null)
            {
                db.ChangeTracker.Clear();
                return outcome.Problem;
            }

            var body = JsonSerializer.Serialize(outcome.Body, Json);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = key,
                Operation = operation,
                RequestHash = hash,
                StatusCode = outcome.StatusCode,
                ResponseBody = body,
                CreatedAt = http.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
                PerformedBy = actor,
            });

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Text(body, "application/json", Encoding.UTF8, outcome.StatusCode);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 1)
            {
                // Another request changed the account between our read and our write: run once more on fresh data.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                // Two transfers in opposite directions locked the same two accounts in opposite order; PostgreSQL
                // aborted this one. Like a version conflict: run once more on fresh data.
                db.ChangeTracker.Clear();
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                db.ChangeTracker.Clear();
                return Problems.Of(StatusCodes.Status409Conflict, "concurrency_conflict", "The account changed twice meanwhile; try again.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AccountsDbContext.IdempotencyKey })
            {
                // The same key at the same moment: the other request won, and its answer is now stored.
                db.ChangeTracker.Clear();
                return await ReplayAsync(db, operation, key, hash, ct)
                    ?? throw new InvalidOperationException("The idempotency record vanished after a key conflict.");
            }
        }
    }

    private static async Task<IResult?> ReplayAsync(AccountsDbContext db, string operation, string key, string hash, CancellationToken ct)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Operation == operation && r.Key == key, ct);
        return existing is null ? null
            : existing.RequestHash != hash
                ? Problems.Rule("idempotency_key_reused", "This Idempotency-Key was already used for a different request.")
                : Results.Text(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
    }
}
