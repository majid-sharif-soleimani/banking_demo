using System.Text.Json.Nodes;

namespace Bank.Contracts;

/// <summary>
/// A contract type that describes its own JSON Schema, because the schema generator cannot see its shape. Every type
/// with a custom <c>JsonConverter</c> implements it, next to the converter, describing what the converter writes.
/// </summary>
public interface IJsonSchemaProvider
{
    /// <summary>The JSON Schema of this type's JSON. Returns a new object on every call.</summary>
    static abstract JsonObject JsonSchema();
}
