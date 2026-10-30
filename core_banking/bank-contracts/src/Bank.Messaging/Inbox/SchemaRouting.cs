using Bank.Contracts.Schema;

namespace Bank.Messaging.Inbox;

public enum SchemaMatch
{
    /// <summary>A handler's record has exactly this version.</summary>
    Exact,

    /// <summary>Another ADDITION of a known MODEL; read it with the latest known record of that MODEL.</summary>
    CompatibleAddition,

    /// <summary>The event is handled here, but not in this MODEL; the message cannot be read and goes to the dead-letter topic.</summary>
    UnknownModel,

    /// <summary>No handler here for this event in any version; the message is skipped.</summary>
    NotSubscribed,
}

/// <param name="Known">The handled version to read the message with, for Exact and CompatibleAddition.</param>
public sealed record SchemaRoute(SchemaMatch Match, SchemaKey? Known);

/// <summary>
/// Decides which known record reads an incoming schema version (SchemaVer, bank-contracts ADR 0003): within one MODEL
/// every version only adds optional fields, so any record of that MODEL can read it.
/// </summary>
public static class SchemaRouting
{
    public static SchemaRoute Resolve(SchemaKey incoming, IEnumerable<SchemaKey> known)
    {
        var sameSchema = known.Where(incoming.IsSameSchemaAs).ToList();
        if (sameSchema.Count == 0)
        {
            return new SchemaRoute(SchemaMatch.NotSubscribed, null);
        }

        if (sameSchema.Contains(incoming))
        {
            return new SchemaRoute(SchemaMatch.Exact, incoming);
        }

        var latestInModel = sameSchema.Where(k => k.Model == incoming.Model).Max();
        return latestInModel is null
            ? new SchemaRoute(SchemaMatch.UnknownModel, null)
            : new SchemaRoute(SchemaMatch.CompatibleAddition, latestInModel);
    }
}
