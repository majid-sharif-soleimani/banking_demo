using System.Text.Json.Serialization;

namespace Bank.Contracts;

/// <summary>
/// An amount in one currency. Never rounds on its own: use MoneyRounding (Bank.Common) explicitly.
/// Operations on two different currencies throw <see cref="CurrencyMismatchException"/>.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public sealed record Money(decimal Amount, string Currency) : IComparable<Money>
{
    public string Currency { get; } = ValidateCurrency(Currency);

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor, money.Currency);

    public static Money operator *(decimal factor, Money money) => money * factor;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => $"{Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    internal static bool IsValidCurrency(string? currency) =>
        currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper);

    private static string ValidateCurrency(string currency) =>
        IsValidCurrency(currency)
            ? currency
            : throw new ArgumentException($"Currency must be a three-letter ISO 4217 code in upper case, got '{currency}'.", nameof(Currency));

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }
}
