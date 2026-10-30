using System.Collections.Concurrent;
using Bank.Messaging.Transport;

namespace Bank.Testing;

/// <summary>
/// A broker in memory: every sent message is recorded and handed to each subscriber, usually another test service's
/// MessagePipeline.ProcessAsync. As with a real broker, a failing consumer does not fail the send; its exception is
/// collected in <see cref="DeliveryErrors"/>.
/// </summary>
public sealed class InMemoryTransport : IMessageTransport
{
    private readonly ConcurrentQueue<TransportMessage> sent = new();
    private readonly ConcurrentQueue<Exception> deliveryErrors = new();
    private readonly List<Func<TransportMessage, CancellationToken, Task>> subscribers = [];

    public IReadOnlyCollection<TransportMessage> Sent => sent;

    public IReadOnlyCollection<Exception> DeliveryErrors => deliveryErrors;

    /// <summary>When true, every send throws, as if the broker were down.</summary>
    public bool FailSends { get; set; }

    public void Subscribe(Func<TransportMessage, CancellationToken, Task> subscriber)
    {
        lock (subscribers)
        {
            subscribers.Add(subscriber);
        }
    }

    public async Task SendAsync(TransportMessage message, CancellationToken ct)
    {
        if (FailSends)
        {
            throw new InvalidOperationException("The in-memory broker is down (FailSends).");
        }

        sent.Enqueue(message);

        Func<TransportMessage, CancellationToken, Task>[] targets;
        lock (subscribers)
        {
            targets = [.. subscribers];
        }

        foreach (var subscriber in targets)
        {
            try
            {
                await subscriber(message, ct);
            }
            catch (Exception ex)
            {
                deliveryErrors.Enqueue(ex);
            }
        }
    }
}
