using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Bank.Agent.Eval;
using Bank.Agent.Host.Features.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// The evaluation set (phase 2 stage 6): fixed conversations against the real model, graded by rules. Run it after a
// change to the instructions, the tools or the model, and compare the score with the last run (eval/results/).
//
//   dotnet run --project agent/eval/Bank.Agent.Eval                 # every case once
//   dotnet run --project agent/eval/Bank.Agent.Eval -- --case fa-balances --repeat 3
//
// It costs real money: every case is a few calls of the model in Agent:Model:Name.
var config = new ConfigurationBuilder().AddUserSecrets<Runner>().AddEnvironmentVariables().Build();
var key = config["Agent:Model:ApiKey"];
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Agent:Model:ApiKey is not set. Set it once with:");
    Console.Error.WriteLine("  dotnet user-secrets set \"Agent:Model:ApiKey\" \"<your key>\" --project agent/eval/Bank.Agent.Eval");
    return 2;
}

// The same defaults as the host (ModelOptions), so the evaluation measures what runs.
var defaults = new ModelOptions();
var modelName = config["Agent:Model:Name"] ?? defaults.Name;
var readerName = config["Agent:Model:ReaderName"] ?? defaults.ReaderName;
var only = Arg("--case");
var repeat = int.TryParse(Arg("--repeat"), out var r) && r > 0 ? r : 1;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var cases = JsonSerializer.Deserialize<EvalCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cases.json")), json)!
    .Where(c => only is null || c.Id == only).ToList();
var anthropic = new AnthropicClient { ApiKey = key };
var runner = new Runner(anthropic.AsIChatClient(modelName), anthropic.AsIChatClient(readerName), defaults.MaxOutputTokens, defaults.HistoryMessages);

var results = new List<object>();
var passed = 0;
foreach (var c in cases)
{
    for (var i = 0; i < repeat; i++)
    {
        var clock = Stopwatch.StartNew();
        IReadOnlyList<string> failures;
        EvalRun? run = null;
        try
        {
            run = await runner.RunAsync(c, CancellationToken.None);
            failures = Grader.Check(c, run);
        }
        catch (Exception ex)
        {
            failures = [$"the run failed: {ex.GetType().Name}"];
        }

        passed += failures.Count == 0 ? 1 : 0;
        Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}  {c.Id,-28} {clock.Elapsed.TotalSeconds,5:0.0}s  {string.Join("; ", failures)}");
        results.Add(new { c.Id, passed = failures.Count == 0, failures, run?.ToolsCalled, run?.Shown, run?.Answer });
    }
}

var total = cases.Count * repeat;
Console.WriteLine($"\n{passed} of {total} passed ({modelName}).");
var folder = Path.Combine(Directory.GetCurrentDirectory(), "agent", "eval", "results");
if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "agent", "eval")))
{
    Directory.CreateDirectory(folder);
    var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    File.WriteAllText(file, JsonSerializer.Serialize(new { model = modelName, passed, total, results }, json));
    Console.WriteLine($"Details: {Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}");
}

return passed == total ? 0 : 1;

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
