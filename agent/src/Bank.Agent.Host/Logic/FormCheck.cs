using System.Globalization;
using System.Text.Json;

namespace Bank.Agent.Host.Logic;

/// <summary>One field of a form the agent shows (phase 2 design plan, section 5).</summary>
/// <param name="Type">text, number, date or choice.</param>
public sealed record FormField(string Name, string Label, string Type, bool Required, IReadOnlyList<string>? Options, decimal? Min, decimal? Max);

/// <summary>
/// Checks a form twice: its definition when the model asks to show it, and the person's values when they send it. The
/// browser checks the same things for a quick answer, but only this check counts: a browser can be changed, the host
/// cannot.
/// </summary>
public static class FormCheck
{
    public static readonly string[] Types = ["text", "number", "date", "choice"];

    public const int MaxTextLength = 500;

    /// <returns>Null when the form can be shown, otherwise what is wrong, written for the model.</returns>
    public static string? Definition(IReadOnlyList<FormField> fields)
    {
        if (fields.Count == 0)
        {
            return "A form needs at least one field.";
        }

        if (fields.Any(f => string.IsNullOrWhiteSpace(f.Name)) || fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            return "Every field needs a name, and the names must differ.";
        }

        if (fields.FirstOrDefault(f => !Types.Contains(f.Type)) is { } unknown)
        {
            return $"Field '{unknown.Name}' has the unknown type '{unknown.Type}'. Use one of: {string.Join(", ", Types)}.";
        }

        return fields.FirstOrDefault(f => f.Type == "choice" && (f.Options is null || f.Options.Count == 0)) is { } choice
            ? $"The choice field '{choice.Name}' needs options."
            : null;
    }

    /// <summary>The person's values, checked against the form. Returns one message per wrong field; empty when all are fine.</summary>
    public static IReadOnlyDictionary<string, string> Values(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in values.Keys.Where(k => fields.All(f => f.Name != k)))
        {
            errors[name] = "This field is not part of the form.";
        }

        foreach (var field in fields)
        {
            var value = values.GetValueOrDefault(field.Name)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                if (field.Required)
                {
                    errors[field.Name] = "Required.";
                }

                continue;
            }

            if (Problem(field, value) is { } problem)
            {
                errors[field.Name] = problem;
            }
        }

        return errors;
    }

    /// <summary>The numbers the person typed in a form whose values have passed <see cref="Values"/>.</summary>
    public static IReadOnlyList<decimal> Numbers(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string?> values) =>
    [
        .. fields.Where(f => f.Type == "number")
            .Select(f => values.GetValueOrDefault(f.Name)?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => decimal.Parse(v!, NumberStyles.Number, CultureInfo.InvariantCulture)),
    ];

    /// <returns>Null when the tool's amount is one the person entered, or they entered none; otherwise what is wrong, written for the model.</returns>
    public static string? DiffersFromEntered(IReadOnlyList<decimal> entered, JsonElement arguments)
    {
        if (entered.Count == 0)
        {
            return null;
        }

        var amount = arguments.TryGetProperty("amount", out var value) ? Decimal(value) : null;
        return amount is { } known && entered.Contains(known)
            ? null
            : $"Not prepared, the person sees no card: the amount is not what they entered in the form ({string.Join(", ", entered.Select(n => n.ToString(CultureInfo.InvariantCulture)))}). " +
              "Call this tool again with their amount. It may be less than the installment. If you think they meant another amount, say why and ask with show_form.";
    }

    private static decimal? Decimal(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static string? Problem(FormField field, string value)
    {
        if (value.Length > MaxTextLength)
        {
            return $"At most {MaxTextLength} characters.";
        }

        switch (field.Type)
        {
            case "number":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return "Write a number, such as 1250.50.";
                }

                if (number < field.Min)
                {
                    return $"At least {field.Min.Value.ToString(CultureInfo.InvariantCulture)}.";
                }

                return number > field.Max ? $"At most {field.Max.Value.ToString(CultureInfo.InvariantCulture)}." : null;
            case "date":
                return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "Write a date as yyyy-MM-dd.";
            case "choice":
                return field.Options!.Contains(value) ? null : "Pick one of the options.";
            default:
                return null;
        }
    }
}
