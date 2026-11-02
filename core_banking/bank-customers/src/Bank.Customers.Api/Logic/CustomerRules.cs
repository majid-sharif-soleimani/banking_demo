using System.Net.Mail;

namespace Bank.Customers.Api.Logic;

/// <summary>What a registration or contact update says about a customer, before it is stored.</summary>
public sealed record CustomerInput(
    string Name, string NationalId, DateOnly DateOfBirth, string Email, string Phone, string Address, string PreferredLanguage);

/// <summary>
/// Input rules for customers. There is no KYC in phase 1 (design plan section 2): only that the values are present, fit
/// their columns and are well-formed. Errors are keyed by the camelCase field name, ready for a validation problem.
/// </summary>
public static class CustomerRules
{
    public const string DefaultLanguage = "en";

    /// <summary>English by default, Swedish and Persian supported (design plan ADR 24).</summary>
    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal) { "en", "sv", "fa" };

    public static string LanguageOrDefault(string? language) => string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;

    public static Dictionary<string, string[]> Validate(CustomerInput input, DateOnly businessDate)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, "name", input.Name, 200);
        Required(errors, "nationalId", input.NationalId, 32);
        Required(errors, "phone", input.Phone, 32);
        Required(errors, "address", input.Address, 500);
        ValidateContact(errors, input.Email, input.PreferredLanguage);

        if (input.DateOfBirth >= businessDate)
        {
            errors["dateOfBirth"] = ["The date of birth must be before the business date."];
        }

        return errors;
    }

    /// <summary>The rules for the fields a customer may change later (email and language; phone and address are free text).</summary>
    public static void ValidateContact(Dictionary<string, string[]> errors, string? email, string? language)
    {
        if (email is not null && !IsEmail(email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (language is not null && !Languages.Contains(language))
        {
            errors["preferredLanguage"] = [$"Supported languages: {string.Join(", ", Languages)}."];
        }
    }

    private static void Required(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["A value is required."];
        }
        else if (value.Length > maxLength)
        {
            errors[field] = [$"At most {maxLength} characters."];
        }
    }

    private static bool IsEmail(string value) =>
        value.Length <= 254 && MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.');
}
