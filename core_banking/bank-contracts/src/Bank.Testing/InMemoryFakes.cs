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

    public Task<ExchangeRate> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct) =>
        from == to ? Task.FromResult(new ExchangeRate(from, to, 1m, "test", date))
        : rates.TryGetValue((from, to), out var rate) ? Task.FromResult(new ExchangeRate(from, to, rate, "test", date))
        : throw new ExchangeRateUnavailableException(from, to);
}

/// <summary>
/// Valid but predictable numbers for tests: 10000001, 10000002, … plus their Luhn check digit (9 digits, like the real
/// generator in Accounts, which draws the 8 digits at random).
/// </summary>
public sealed class SequentialAccountNumberGenerator : IAccountNumberGenerator
{
    private int last = 10_000_000;

    public Task<string> NextAsync(CancellationToken ct)
    {
        var body = Interlocked.Increment(ref last).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Task.FromResult(body + LuhnCheckDigit(body));
    }

    private static int LuhnCheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            // From the right, every first digit (the one next to the check digit) is doubled.
            sum += i % 2 == 0 ? (digit * 2 > 9 ? digit * 2 - 9 : digit * 2) : digit;
        }

        return (10 - sum % 10) % 10;
    }
}
