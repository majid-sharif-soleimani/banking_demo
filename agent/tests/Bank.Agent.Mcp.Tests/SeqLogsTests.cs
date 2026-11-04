using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Agent.Mcp.Tests;

/// <summary>
/// Logs reach Seq when Seq:ServerUrl is set (Bank.ServiceDefaults, bank-platform ADR 0005). A small web server plays Seq, so
/// the test checks what really goes over the wire: Seq's OTLP path, in the protobuf format Seq reads.
/// </summary>
public class SeqLogsTests
{
    [Fact]
    public async Task With_a_Seq_address_the_logs_go_to_its_OTLP_path()
    {
        var received = new ConcurrentQueue<(string Path, string? ContentType)>();
        await using var seq = WebApplication.CreateSlimBuilder().Build();
        seq.Urls.Add("http://127.0.0.1:0");
        seq.Run(async http =>
        {
            received.Enqueue((http.Request.Path, http.Request.ContentType));
            await http.Request.Body.CopyToAsync(Stream.Null);
        });
        await seq.StartAsync(TestContext.Current.CancellationToken);

        var app = new McpServerApp().WithWebHostBuilder(host => host.UseSetting("Seq:ServerUrl", seq.Urls.Single()));
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogWarning("A line for Seq");

        // Stopping the host flushes the batch of logs it still holds.
        await app.DisposeAsync();

        // The server side of that last send can finish a moment after the host has stopped.
        for (var waited = 0; received.IsEmpty && waited < 50; waited++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        received.ShouldContain(r => r.Path == "/ingest/otlp/v1/logs" && r.ContentType == "application/x-protobuf");
    }
}
