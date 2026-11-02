using Bank.DataImport;
using Bank.DataImport.Source;
using Microsoft.Extensions.Configuration;

// Usage: dotnet run --project src/Bank.DataImport -- --gateway http://localhost:5100 [--data data/raw] [--force]
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args.Where(a => a != "--force").ToArray())
    .Build();

var gateway = configuration["gateway"] ?? configuration["Import:Gateway"];
if (string.IsNullOrWhiteSpace(gateway))
{
    Console.Error.WriteLine("Pass --gateway <url>: the gateway's address from the Aspire dashboard.");
    return 2;
}

SourceData source;
var folder = configuration["data"] ?? configuration["Import:Data"] ?? "data/raw";
try
{
    source = CsvSource.Load(folder, configuration.GetSection("Import:Columns"));
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

var options = new ImportOptions(
    AmountFactor: configuration.GetValue("Import:AmountFactor", 10.0m),
    Customers: configuration.GetValue("Import:Customers", 100),
    Seed: configuration.GetValue("Import:Seed", 20260101),
    Force: args.Contains("--force"));

using var http = new HttpClient { BaseAddress = new Uri(gateway), Timeout = TimeSpan.FromMinutes(2) };
var importer = new Importer(http, options, Console.Out);
var reportPath = configuration["Import:Report"] ?? "data/import-report.md";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
try
{
    await importer.RunAsync(source, CancellationToken.None);
}
catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException)
{
    // The report so far still tells what was imported before the stop.
    importer.Report.VerificationErrors.Add($"import stopped: {ex.Message}");
    await File.WriteAllTextAsync(reportPath, importer.Report.ToMarkdown());
    Console.Error.WriteLine($"Import stopped: {ex.Message} Partial report written to {reportPath}.");
    return 4;
}

var report = importer.Report;
await File.WriteAllTextAsync(reportPath, report.ToMarkdown());
Console.WriteLine($"Report written to {reportPath}. Verification {(report.Verified ? "passed" : "FAILED")}.");
return report.Verified ? 0 : 1;
