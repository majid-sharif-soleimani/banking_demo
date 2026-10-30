using Bank.Contracts.SchemaTool;

namespace Bank.Contracts.Tests;

/// <summary>
/// Proves the snapshot tool writes only missing files and never overwrites a published one (ADR 0004).
/// Runs against a temporary copy of the folder layout, never the real schemas/ folder.
/// </summary>
public sealed class SchemaSnapshotsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("schema-snapshots-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Check_reports_missing_snapshots_and_writes_nothing()
    {
        var results = SchemaSnapshots.Run(_folder, write: false);

        results.ShouldAllBe(r => r.Status == SnapshotStatus.Missing);
        Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public void Write_writes_missing_snapshots_at_their_schema_path()
    {
        var results = SchemaSnapshots.Run(_folder, write: true);

        results.ShouldAllBe(r => r.Status == SnapshotStatus.Written);
        results.ShouldAllBe(r => File.Exists(Path.Combine(_folder, r.Key.ResourcePath)));
    }

    [Fact]
    public void A_written_snapshot_matches_on_the_next_run()
    {
        SchemaSnapshots.Run(_folder, write: true);

        SchemaSnapshots.Run(_folder, write: false).ShouldAllBe(r => r.Status == SnapshotStatus.Matches);
    }

    [Fact]
    public void Write_never_overwrites_a_snapshot_that_differs()
    {
        var first = SchemaSnapshots.Run(_folder, write: true)[0];
        const string Edited = """{ "edited": true }""";
        File.WriteAllText(first.Path, Edited);

        var again = SchemaSnapshots.Run(_folder, write: true)[0];

        again.Status.ShouldBe(SnapshotStatus.Differs);
        File.ReadAllText(first.Path).ShouldBe(Edited);
    }

    [Fact]
    public void Written_snapshots_equal_the_committed_ones()
    {
        var results = SchemaSnapshots.Run(_folder, write: true);

        foreach (var result in results)
        {
            File.ReadAllText(result.Path).ShouldBe(ContractSchemas.Read(result.Key), $"{result.Key} is not written byte for byte as committed.");
        }
    }
}
