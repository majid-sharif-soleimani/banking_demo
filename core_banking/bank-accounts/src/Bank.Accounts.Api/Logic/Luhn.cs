namespace Bank.Accounts.Api.Logic;

/// <summary>
/// The Luhn (mod 10) check digit (design plan section 2, account numbers). It catches every single-digit typo and almost
/// every swap of two neighbouring digits.
/// </summary>
public static class Luhn
{
    /// <summary>The digit to append to <paramref name="body"/>, which must be all digits.</summary>
    public static int CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            if (digit is < 0 or > 9)
            {
                throw new ArgumentException("Only digits are allowed.", nameof(body));
            }

            // From the right, the digit next to the (future) check digit is doubled, then every second one.
            if (i % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return (10 - sum % 10) % 10;
    }

    /// <summary>True when <paramref name="number"/> is digits only and its last digit is the Luhn check digit of the rest.</summary>
    public static bool IsValid(string number) =>
        number.Length >= 2 && number.All(char.IsAsciiDigit) && CheckDigit(number[..^1]) == number[^1] - '0';
}

/// <summary>Account numbers: 9 digits without meaning, 8 random digits (the first 1–9) plus a Luhn check digit.</summary>
public static class AccountNumbers
{
    public const int MinBody = 10_000_000;
    public const int MaxBodyExclusive = 100_000_000;

    public static string Compose(int body)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(body, MinBody);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, MaxBodyExclusive);
        var digits = body.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return digits + CheckDigit(digits);
    }

    private static int CheckDigit(string digits) => Luhn.CheckDigit(digits);
}
