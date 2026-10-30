using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bank.Contracts.Schema;

namespace Bank.Contracts.SchemaTool;

public enum SnapshotStatus
{
    Matches,
    Missing,
    Written,
    Differs,
}

public sealed record SnapshotResult(Type EventType, SchemaKey Key, string Path, SnapshotStatus Status);

/// <summary>
/// Compares the schema generated from each event record with its snapshot file under <c>schemas/</c>, and writes the
/// missing ones on request (ADR 0004). An existing file is never overwritten: a published version is immutable.
/// </summary>
public static class SchemaSnapshots
{
    private static readonly JsonSerializerOptions FileFormat = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToFileText(JsonObject schema) => schema.ToJsonString(FileFormat) + "\n";

    /// <summary>Checks every event record against its snapshot in <paramref name="contractsFolder"/>.</summary>
    /// <param name="write">When true, a missing snapshot is written instead of reported.</param>
    public static IReadOnlyList<SnapshotResult> Run(string contractsFolder, bool write) =>
        ContractCatalog.EventTypes.Select(type => Run(contractsFolder, type, write)).ToList();

    private static SnapshotResult Run(string contractsFolder, Type eventType, bool write)
    {
        var uri = ContractCatalog.SchemaUriOf(eventType)
            ?? throw new InvalidOperationException($"{eventType.Name} needs 'public const string SchemaUri'.");
        var key = SchemaKey.Parse(uri);
        var generated = ContractSchemaGenerator.Generate(eventType, key);
        var path = Path.Combine(contractsFolder, key.ResourcePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(path))
        {
            var published = JsonNode.Parse(File.ReadAllText(path));
            var status = JsonNode.DeepEquals(published, generated) ? SnapshotStatus.Matches : SnapshotStatus.Differs;
            return new SnapshotResult(eventType, key, path, status);
        }

        if (!write)
        {
            return new SnapshotResult(eventType, key, path, SnapshotStatus.Missing);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToFileText(generated));
        return new SnapshotResult(eventType, key, path, SnapshotStatus.Written);
    }

    /// <summary>The bank-contracts folder: the first folder above <paramref name="start"/> that holds Bank.Contracts.slnx.</summary>
    public static string ContractsFolder(string start)
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Bank.Contracts.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not find the bank-contracts folder (Bank.Contracts.slnx) above {start}.");
    }
}
