using System.Reflection;

namespace Bank.Toolkit.Data.Migrations;

/// <summary>
/// Loads SQL scripts embedded in an assembly with <c>LogicalName="migrations/%(Filename)%(Extension)"</c>,
/// ordered by file name, so name them 0001_..., 0002_....
/// </summary>
public static class MigrationScripts
{
    private const string Prefix = "migrations/";

    /// <param name="source">Prefix of each script id, so that two assemblies can both have a 0001_ script.</param>
    public static IReadOnlyList<MigrationScript> FromAssembly(Assembly assembly, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => new MigrationScript($"{source}/{name[Prefix.Length..]}", Read(assembly, name)))
            .ToList();
    }

    private static string Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
