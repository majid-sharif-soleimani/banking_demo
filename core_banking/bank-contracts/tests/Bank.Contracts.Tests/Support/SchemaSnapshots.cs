using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bank.Contracts.Tests.Support;

/// <summary>
/// Writes generated schemas as snapshot files under <c>schemas/</c> (ADR 0003). Writing happens only when the
/// environment variable <see cref="WriteVariable"/> is 1, and an existing file is never overwritten:
/// a published version is immutable.
/// </summary>
public static class SchemaSnapshots
{
    public const string WriteVariable = "WRITE_SCHEMA_SNAPSHOTS";

    private static readonly JsonSerializerOptions FileFormat = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool WritingEnabled => Environment.GetEnvironmentVariable(WriteVariable) == "1";

    public static string ToFileText(JsonObject schema) => schema.ToJsonString(FileFormat) + "\n";

    /// <summary>Writes the snapshot if it does not exist yet, and returns the file path.</summary>
    public static string WriteIfMissing(SchemaKey key, JsonObject schema)
    {
        var path = Path.Combine(ContractsFolder(), key.ResourcePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToFileText(schema));
        }

        return path;
    }

    private static string ContractsFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Bank.Contracts.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the bank-contracts folder (Bank.Contracts.slnx) above the test output.");
    }
}
