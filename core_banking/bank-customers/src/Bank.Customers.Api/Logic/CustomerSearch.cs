using System.Text;

namespace Bank.Customers.Api.Logic;

/// <summary>The search filters; every given one must match (AND). <paramref name="CustomerNumber"/> is the customer id.</summary>
public sealed record CustomerSearchFilter(string? Name, string? Email, string? Phone, Guid? CustomerNumber, string? NationalId = null);

/// <summary>
/// How the customer search matches (bank-customers ADR 0001): name contains the term, case-insensitive; email equal,
/// case-insensitive; phone equal on digits only; customer number equal; national id equal after trimming (the login of
/// bank-identity, workspace ADR 0007). Pure input handling; the query is in the feature.
/// </summary>
public static class CustomerSearch
{
    /// <summary>One letter would match almost everyone.</summary>
    public const int MinNameLength = 2;

    /// <summary>Fewer digits would match by accident.</summary>
    public const int MinPhoneDigits = 5;

    /// <summary>"+46 70-000 00 00" → "46700000000". The stored phone is compared the same way.</summary>
    public static string PhoneDigits(string phone) => string.Concat(phone.Where(char.IsAsciiDigit));

    /// <summary>An ILIKE pattern "%term%" with the term's own %, _ and \ escaped, so they match literally (escape character \).</summary>
    public static string ContainsPattern(string term)
    {
        var pattern = new StringBuilder("%");
        foreach (var c in term.Trim())
        {
            if (c is '%' or '_' or '\\')
            {
                pattern.Append('\\');
            }

            pattern.Append(c);
        }

        return pattern.Append('%').ToString();
    }

    /// <summary>Errors keyed by field, in the shape of a validation problem. Empty when the filter can run.</summary>
    public static Dictionary<string, string[]> Validate(CustomerSearchFilter filter)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(filter.Name) && string.IsNullOrWhiteSpace(filter.Email)
            && string.IsNullOrWhiteSpace(filter.Phone) && filter.CustomerNumber is null && string.IsNullOrWhiteSpace(filter.NationalId))
        {
            errors["filter"] = ["Give at least one of name, email, phone, customerNumber or nationalId."];
            return errors;
        }

        if (filter.Name is { } name && !string.IsNullOrWhiteSpace(name) && name.Trim().Length < MinNameLength)
        {
            errors["name"] = [$"A name term needs at least {MinNameLength} characters."];
        }

        if (filter.Phone is { } phone && !string.IsNullOrWhiteSpace(phone) && PhoneDigits(phone).Length < MinPhoneDigits)
        {
            errors["phone"] = [$"A phone number needs at least {MinPhoneDigits} digits."];
        }

        return errors;
    }
}
