using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;

namespace Bank.Operations.IntegrationTests.Support;

/// <summary>
/// Stands in for Accounts and Lending until they exist: when Operations sends DayClosed(D), it replies with
/// DayCompleted(D, service) for each of its services, through Operations' inbox pipeline, as Kafka would deliver it.
/// </summary>
public static class StubParticipant
{
    public static void ReplyToDayClosed(this OperationsApp app, params string[] services) =>
        app.Broker.Subscribe(async (message, ct) =>
        {
            if (message.SchemaUri != DayClosed.SchemaUri)
            {
                return;
            }

            var closed = JsonSerializer.Deserialize<DayClosed>(message.Payload, ContractJson.Options)!;
            foreach (var service in services)
            {
                await app.DeliverAsync(Completed(closed.Date, service), DayCompleted.SchemaUri, closed.Date.ToString("yyyy-MM-dd"), ct);
            }
        });

    public static DayCompleted Completed(DateOnly date, string service) =>
        new(Guid.NewGuid(), date, service, date, service);
}
