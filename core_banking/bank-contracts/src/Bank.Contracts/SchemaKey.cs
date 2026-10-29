using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Bank.Contracts;

/// <summary>
/// An Iglu-style schema URI, <c>iglu:{vendor}/{name}/{format}/{MODEL}-{REVISION}-{ADDITION}</c>, versioned with SchemaVer.
/// MODEL: breaking change. REVISION: may break some historical data. ADDITION: compatible with all historical data.
/// </summary>
public sealed record SchemaKey(string Vendor, string Name, string Format, int Model, int Revision, int Addition)
    : IComparable<SchemaKey>
{
    private const string Prefix = "iglu:";

    public string Version => $"{Model}-{Revision}-{Addition}";

    /// <summary>Path of the schema file below the bank-contracts folder, and its embedded resource name.</summary>
    public string ResourcePath => $"schemas/{Vendor}/{Name}/{Format}/{Version}.json";

    public static SchemaKey Parse(string uri) =>
        TryParse(uri, out var key) ? key : throw new FormatException($"'{uri}' is not a valid schema URI like iglu:se.bank/day_closed/jsonschema/1-0-0.");

    public static bool TryParse(string? uri, [NotNullWhen(true)] out SchemaKey? key)
    {
        key = null;
        if (uri is null || !uri.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = uri[Prefix.Length..].Split('/');
        if (parts.Length != 4 || parts[..3].Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var version = parts[3].Split('-');
        if (version.Length != 3
            || !TryParsePart(version[0], out var model) || model < 1
            || !TryParsePart(version[1], out var revision)
            || !TryParsePart(version[2], out var addition))
        {
            return false;
        }

        key = new SchemaKey(parts[0], parts[1], parts[2], model, revision, addition);
        return true;
    }

    /// <summary>Builds the key from a path such as <c>schemas/se.bank/day_closed/jsonschema/1-0-0.json</c>.</summary>
    public static SchemaKey FromResourcePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length != 5 || parts[0] != "schemas" || !parts[4].EndsWith(".json", StringComparison.Ordinal))
        {
            throw new FormatException($"'{path}' is not laid out as schemas/<vendor>/<name>/<format>/<version>.json.");
        }

        return Parse($"{Prefix}{parts[1]}/{parts[2]}/{parts[3]}/{parts[4][..^".json".Length]}");
    }

    /// <summary>True when both keys name the same schema, in any version.</summary>
    public bool IsSameSchemaAs(SchemaKey other) => Vendor == other.Vendor && Name == other.Name && Format == other.Format;

    public int CompareTo(SchemaKey? other)
    {
        if (other is null)
        {
            return 1;
        }

        var bySchema = string.CompareOrdinal($"{Vendor}/{Name}/{Format}", $"{other.Vendor}/{other.Name}/{other.Format}");
        if (bySchema != 0)
        {
            return bySchema;
        }

        return (Model, Revision, Addition).CompareTo((other.Model, other.Revision, other.Addition));
    }

    public override string ToString() => $"{Prefix}{Vendor}/{Name}/{Format}/{Version}";

    private static bool TryParsePart(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
