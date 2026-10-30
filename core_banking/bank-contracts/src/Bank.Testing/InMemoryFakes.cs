using System.Collections.Concurrent;
using Bank.Common;

namespace Bank.Testing;

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> files = new();

    public async Task SaveAsync(string path, Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        files[path] = buffer.ToArray();
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        files.TryGetValue(path, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new FileNotFoundException($"No file at '{path}'.", path);

    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(files.ContainsKey(path));

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        files.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => sent;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Rates set by the test; the same currency is always 1, a missing pair throws.</summary>
public sealed class FixedExchangeRateProvider : IExchangeRateProvider
{
    private readonly ConcurrentDictionary<(string From, string To), decimal> rates = new();

    public FixedExchangeRateProvider Set(string from, string to, decimal rate)
    {
        rates[(from, to)] = rate;
        return this;
    }

    public Task<decimal> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct) =>
        from == to ? Task.FromResult(1m)
        : rates.TryGetValue((from, to), out var rate) ? Task.FromResult(rate)
        : throw new KeyNotFoundException($"No test rate from {from} to {to}.");
}

/// <summary>Numbers TEST-000001, TEST-000002, ...; the real format is still open.</summary>
public sealed class SequentialAccountNumberGenerator : IAccountNumberGenerator
{
    private int last;

    public Task<string> NextAsync(CancellationToken ct) =>
        Task.FromResult($"TEST-{Interlocked.Increment(ref last):D6}");
}
