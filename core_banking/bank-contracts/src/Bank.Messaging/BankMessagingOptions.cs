namespace Bank.Messaging;

public sealed class BankMessagingOptions
{
    /// <summary>The service name, such as "operations"; also its schema and its topic bank.&lt;service&gt;.events.</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>How long the dispatcher waits after a poll that found less than a full batch.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How many outbox rows one poll locks and sends.</summary>
    public int BatchSize { get; set; } = 100;
}
