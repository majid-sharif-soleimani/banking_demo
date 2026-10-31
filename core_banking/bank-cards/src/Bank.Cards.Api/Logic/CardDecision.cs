namespace Bank.Cards.Api.Logic;

/// <summary>The behaviour of a test card (implementation-decisions, "Test cards"). Numbers start with 9999, never issued to real cards.</summary>
public enum CardBehavior
{
    Approve,
    Decline,
    InsufficientFunds,
}

/// <summary>What the simulated card network answers: captured, or declined with a reason. The card itself has no balance.</summary>
public sealed record CardDecision(bool Captured, string? Reason)
{
    public static CardDecision For(CardBehavior behavior) => behavior switch
    {
        CardBehavior.Approve => new(true, null),
        CardBehavior.Decline => new(false, "declined"),
        CardBehavior.InsufficientFunds => new(false, "insufficient_funds"),
        _ => throw new ArgumentOutOfRangeException(nameof(behavior)),
    };

    /// <summary>Digits only: "9999 0000 0000 0001" and "9999-0000-0000-0001" are the same card.</summary>
    public static string Normalize(string number) => new([.. number.Where(char.IsAsciiDigit)]);

    /// <summary>A card number as it may appear in responses and logs.</summary>
    public static string Mask(string number) => new string('*', Math.Max(0, number.Length - 4)) + number[^4..];
}
