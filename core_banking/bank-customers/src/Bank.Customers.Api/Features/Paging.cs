using System.Buffers.Text;
using System.Text.Json;
using Bank.Contracts;

namespace Bank.Customers.Api.Features;

/// <summary>One page of a list. <paramref name="NextCursor"/> is null on the last page (workspace ADR 0004).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Keyset paging (workspace ADR 0004): the cursor is base64url JSON with the sort values of the last row returned, opaque to
/// clients. Read limit + 1 rows; the extra one only says whether there is a next page. The same contract, copied, lives in
/// Lending and Accounts.
/// </summary>
public static class Paging
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    // A cursor missing a sort value ("{}") is invalid, not a key full of defaults.
    private static readonly JsonSerializerOptions CursorJson = new(ContractJson.Options) { RespectRequiredConstructorParameters = true };

    public static IResult? LimitProblem(int? limit) => limit is null or (>= 1 and <= MaxLimit)
        ? null
        : Problems.Of(StatusCodes.Status400BadRequest, "invalid_limit", $"'limit' must be between 1 and {MaxLimit}.");

    public static IResult InvalidCursor() =>
        Problems.Of(StatusCodes.Status400BadRequest, "invalid_cursor", "The cursor is not one this list returned.");

    /// <summary>Null when there is no cursor (first page) or it cannot be read; check <paramref name="cursor"/> to tell them apart.</summary>
    public static TKey? Decode<TKey>(string? cursor) where TKey : class
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TKey>(Base64Url.DecodeFromChars(cursor), CursorJson);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <param name="rows">Up to limit + 1 rows, already in the list's sort order.</param>
    public static Page<TItem> ToPage<TRow, TItem, TKey>(IReadOnlyList<TRow> rows, int limit, Func<TRow, TItem> map, Func<TRow, TKey> key)
    {
        var items = rows.Take(limit).ToList();
        var next = rows.Count > limit
            ? Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(key(items[^1]), CursorJson))
            : null;
        return new Page<TItem>([.. items.Select(map)], next);
    }
}
