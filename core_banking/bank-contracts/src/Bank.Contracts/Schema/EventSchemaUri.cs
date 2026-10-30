using System.Collections.Concurrent;
using System.Reflection;

namespace Bank.Contracts.Schema;

/// <summary>Reads the <c>public const string SchemaUri</c> that every integration event record declares.</summary>
public static class EventSchemaUri
{
    private static readonly ConcurrentDictionary<Type, string?> Cache = new();

    /// <summary>The record's SchemaUri, or null when the type declares none.</summary>
    public static string? Find(Type eventType) => Cache.GetOrAdd(eventType, static type =>
        type.GetField("SchemaUri", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field
            ? (string?)field.GetRawConstantValue()
            : null);

    public static SchemaKey Of(Type eventType) =>
        Find(eventType) is { } uri
            ? SchemaKey.Parse(uri)
            : throw new InvalidOperationException($"{eventType.Name} declares no 'public const string SchemaUri'.");
}
