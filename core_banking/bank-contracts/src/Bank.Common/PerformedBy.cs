namespace Bank.Common;

/// <summary>
/// The actor id stored in every PerformedBy column and event field. It comes from the X-Performed-By header;
/// when it is missing the actor is "system".
/// </summary>
public static class PerformedBy
{
    public const string System = "system";

    public const string HeaderName = "X-Performed-By";

    public const int MaxLength = 100;

    public static string From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System;
        }

        var actor = value.Trim();
        return actor.Length <= MaxLength
            ? actor
            : throw new ArgumentException($"PerformedBy must be at most {MaxLength} characters.", nameof(value));
    }
}
