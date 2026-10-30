using System.Reflection;

namespace Bank.Contracts.SchemaTool;

/// <summary>Finds every integration event record in Bank.Contracts by reflection.</summary>
public static class ContractCatalog
{
    public static IReadOnlyList<Type> EventTypes { get; } = typeof(IIntegrationEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    public static string? SchemaUriOf(Type eventType) =>
        eventType.GetField("SchemaUri", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field
            ? (string?)field.GetRawConstantValue()
            : null;
}
