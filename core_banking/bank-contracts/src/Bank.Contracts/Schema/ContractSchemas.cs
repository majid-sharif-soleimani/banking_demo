using System.Reflection;

namespace Bank.Contracts.Schema;

/// <summary>The JSON Schemas shipped inside this package as embedded resources.</summary>
public static class ContractSchemas
{
    private static readonly Assembly Assembly = typeof(ContractSchemas).Assembly;

    // Resource names keep the folder separator of the machine that built the package, so map them by key.
    private static readonly IReadOnlyDictionary<SchemaKey, string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith("schemas", StringComparison.Ordinal))
        .ToDictionary(SchemaKey.FromResourcePath);

    public static IReadOnlyList<SchemaKey> All { get; } = ResourceNames.Keys.Order().ToList();

    public static string Read(SchemaKey key)
    {
        if (!ResourceNames.TryGetValue(key, out var resourceName))
        {
            throw new KeyNotFoundException($"No schema {key} in Bank.Contracts.");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
