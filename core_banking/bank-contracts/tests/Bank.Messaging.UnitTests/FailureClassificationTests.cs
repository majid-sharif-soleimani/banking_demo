using System.Text.Json;
using Bank.Messaging.Inbox;
using Bank.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;

namespace Bank.Messaging.UnitTests;

public class FailureClassificationTests
{
    public static TheoryData<Exception> Permanent =>
    [
        new UnsupportedSchemaVersionException("iglu:se.bank/test_event/jsonschema/2-0-0"),
        new JsonException("bad payload"),
        new InvalidMessageException("missing header message-id"),
    ];

    public static TheoryData<Exception> Transient =>
    [
        new TimeoutException("database timeout"),
        new InvalidOperationException("handler failed"),
        // A business-key violation is retried and then dead-lettered like any other failure (Majid, ADR 0013).
        new DbUpdateException("duplicate key value violates unique constraint"),
    ];

    [Theory]
    [MemberData(nameof(Permanent))]
    public void Errors_that_can_never_succeed_are_permanent(Exception error)
    {
        FailureClassification.IsPermanent(error).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Transient))]
    public void Every_other_error_is_retried(Exception error)
    {
        FailureClassification.IsPermanent(error).ShouldBeFalse();
    }
}
