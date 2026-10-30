using Bank.Contracts.SchemaTool;

// Usage, from core_banking/bank-contracts:
//   dotnet run --project tools/Bank.Contracts.SchemaTool -- write   writes missing snapshots
//   dotnet run --project tools/Bank.Contracts.SchemaTool -- check   only compares
// Exit code 1 when a snapshot differs from its record, or (check) is missing.
var command = args.FirstOrDefault();
if (command is not ("write" or "check"))
{
    Console.Error.WriteLine("Usage: Bank.Contracts.SchemaTool write|check");
    return 2;
}

var results = SchemaSnapshots.Run(SchemaSnapshots.ContractsFolder(AppContext.BaseDirectory), write: command == "write");

foreach (var result in results)
{
    var message = result.Status switch
    {
        SnapshotStatus.Matches => $"ok       {result.Key}",
        SnapshotStatus.Written => $"written  {result.Key} -> {result.Path}",
        SnapshotStatus.Missing => $"MISSING  {result.Key}: run this tool with 'write'.",
        SnapshotStatus.Differs => $"DIFFERS  {result.Key}: {result.EventType.Name} no longer matches its published schema. " +
            "A published version never changes: bump SchemaUri (ADDITION if you only added optional fields, otherwise a new MODEL), then run 'write'.",
        _ => throw new InvalidOperationException($"Unknown status {result.Status}."),
    };
    Console.WriteLine(message);
}

return results.Any(r => r.Status is SnapshotStatus.Missing or SnapshotStatus.Differs) ? 1 : 0;
