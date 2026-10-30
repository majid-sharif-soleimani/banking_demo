using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.Inbox;
using Bank.Messaging.IntegrationTests.Support;
using Bank.Testing;
using Npgsql;

namespace Bank.Messaging.IntegrationTests;

public class InboxTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<TestService> StartReceiverAsync(bool withAudit = false) =>
        TestService.StartAsync(postgres, "receiver", services =>
        {
            services.AddEventHandler<TestEvent, RecordTestEvent>();
            if (withAudit)
            {
                services.AddEventHandler<TestEvent, AuditTestEvent>();
            }
        });

    [Fact]
    public async Task Duplicate_message_is_applied_once()
    {
        await using var receiver = await StartReceiverAsync();
        var message = TestEvents.AsMessage(TestEvents.New("loan-1"));

        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Handled);
        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Duplicate);

        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
        (await receiver.CountAsync("inbox_message")).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_deliveries_of_one_message_are_applied_once()
    {
        await using var receiver = await StartReceiverAsync();
        var message = TestEvents.AsMessage(TestEvents.New("loan-1"));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => receiver.Pipeline.ProcessAsync(message, Ct)));

        outcomes.Count(o => o == ProcessOutcome.Handled).ShouldBe(1);
        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
    }

    [Fact]
    public async Task Same_fact_under_a_new_message_id_is_stopped_by_the_business_key()
    {
        await using var receiver = await StartReceiverAsync();
        var first = TestEvents.New("loan-1", sequence: 3);
        var again = first with { EventId = Guid.NewGuid() };   // the publisher sent the same fact twice

        await receiver.Pipeline.ProcessAsync(TestEvents.AsMessage(first), Ct);
        var error = await Should.ThrowAsync<PostgresException>(() => receiver.Pipeline.ProcessAsync(TestEvents.AsMessage(again), Ct));

        error.ConstraintName.ShouldBe("uq_received_test_event_business_key");
        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
        (await receiver.CountAsync("inbox_message")).ShouldBe(1);
    }

    [Fact]
    public async Task Handler_effect_inbox_row_and_outgoing_event_commit_together_or_not_at_all()
    {
        await using var receiver = await StartReceiverAsync();
        receiver.Behaviour.PublishReply = true;
        receiver.Behaviour.FailRecord = true;
        var message = TestEvents.AsMessage(TestEvents.New("loan-1"));

        await Should.ThrowAsync<InvalidOperationException>(() => receiver.Pipeline.ProcessAsync(message, Ct));

        (await receiver.CountAsync("received_test_event")).ShouldBe(0);
        (await receiver.CountAsync("inbox_message")).ShouldBe(0);
        (await receiver.CountAsync("outbox_message")).ShouldBe(0);

        // The transport redelivers after the failure; now the handler succeeds.
        receiver.Behaviour.FailRecord = false;
        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Handled);

        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
        (await receiver.CountAsync("inbox_message")).ShouldBe(1);
        (await receiver.CountAsync("outbox_message", "message_key = 'loan-1-reply'")).ShouldBe(1);
    }

    [Fact]
    public async Task Each_handler_applies_a_message_once_even_when_another_handler_failed()
    {
        await using var receiver = await StartReceiverAsync(withAudit: true);
        receiver.Behaviour.FailAudit = true;
        var message = TestEvents.AsMessage(TestEvents.New("loan-1"));

        // RecordTestEvent commits, AuditTestEvent fails: the transport will redeliver the whole message.
        await Should.ThrowAsync<InvalidOperationException>(() => receiver.Pipeline.ProcessAsync(message, Ct));
        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
        (await receiver.CountAsync("audited_test_event")).ShouldBe(0);

        receiver.Behaviour.FailAudit = false;
        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Handled);
        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Duplicate);

        // With an inbox keyed on the message id alone, the retry would either skip the audit or apply the record twice.
        (await receiver.CountAsync("received_test_event")).ShouldBe(1);
        (await receiver.CountAsync("audited_test_event")).ShouldBe(1);
        (await receiver.CountAsync("inbox_message")).ShouldBe(2);
    }

    [Fact]
    public async Task Newer_addition_is_read_with_the_known_record()
    {
        await using var receiver = await StartReceiverAsync();
        var @event = TestEvents.New("loan-1");
        // 1-0-1 added an optional field "note" that this service's 1-0-0 record does not know.
        var payload = $$"""
            {"eventId":"{{@event.EventId}}","occurredOn":"2026-10-30","performedBy":"tester","key":"loan-1","sequence":1,
             "amount":{"amount":"100.50","currency":"SEK"},"note":"added in 1-0-1"}
            """;
        var message = TestEvents.AsMessage(@event, "iglu:se.bank/test_event/jsonschema/1-0-1") with { Payload = payload };

        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.Handled);

        (await receiver.QuerySingleAsync<decimal>("SELECT amount FROM received_test_event")).ShouldBe(100.50m);
        (await receiver.QuerySingleAsync<string>("SELECT schema_uri FROM inbox_message")).ShouldEndWith("1-0-1");
    }

    [Fact]
    public async Task Unknown_model_is_rejected_without_any_change()
    {
        await using var receiver = await StartReceiverAsync();
        var message = TestEvents.AsMessage(TestEvents.New("loan-1"), "iglu:se.bank/test_event/jsonschema/2-0-0");

        await Should.ThrowAsync<UnsupportedSchemaVersionException>(() => receiver.Pipeline.ProcessAsync(message, Ct));

        (await receiver.CountAsync("inbox_message")).ShouldBe(0);
    }

    [Fact]
    public async Task Event_without_a_handler_is_skipped()
    {
        await using var receiver = await StartReceiverAsync();
        var dayClosed = new DayClosed(Guid.NewGuid(), new DateOnly(2026, 10, 30), "system", new DateOnly(2026, 10, 30));
        var message = new Bank.Messaging.Transport.TransportMessage(dayClosed.EventId, DayClosed.SchemaUri, "bank",
            System.Text.Json.JsonSerializer.Serialize(dayClosed, ContractJson.Options), "system", dayClosed.OccurredOn);

        (await receiver.Pipeline.ProcessAsync(message, Ct)).ShouldBe(ProcessOutcome.NotSubscribed);

        (await receiver.CountAsync("inbox_message")).ShouldBe(0);
    }
}
