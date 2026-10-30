using Bank.Contracts.Schema;
using Bank.Messaging.Inbox;

namespace Bank.Messaging.UnitTests;

public class SchemaRoutingTests
{
    private static readonly SchemaKey DayClosed100 = SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/1-0-0");
    private static readonly SchemaKey DayClosed101 = SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/1-0-1");
    private static readonly SchemaKey DayClosed200 = SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/2-0-0");
    private static readonly SchemaKey TestEvent100 = SchemaKey.Parse("iglu:se.bank/test_event/jsonschema/1-0-0");

    [Fact]
    public void Same_version_is_an_exact_match()
    {
        var route = SchemaRouting.Resolve(DayClosed100, [DayClosed100, TestEvent100]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.Exact, DayClosed100));
    }

    [Fact]
    public void Newer_addition_in_a_known_model_is_read_with_the_latest_known_record()
    {
        // 1-0-3 only added optional fields to 1-0-x, so the 1-0-1 record can read it and ignore the new fields.
        var incoming = SchemaKey.Parse("iglu:se.bank/day_closed/jsonschema/1-0-3");

        var route = SchemaRouting.Resolve(incoming, [DayClosed100, DayClosed101]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.CompatibleAddition, DayClosed101));
    }

    [Fact]
    public void Older_addition_is_read_with_the_latest_known_record_of_its_model()
    {
        // Every 1-0-0 message is valid under 1-0-1, because 1-0-1 only adds optional fields.
        var route = SchemaRouting.Resolve(DayClosed100, [DayClosed101]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.CompatibleAddition, DayClosed101));
    }

    [Fact]
    public void Each_model_is_routed_to_its_own_record()
    {
        var route = SchemaRouting.Resolve(DayClosed200, [DayClosed101, DayClosed200]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.Exact, DayClosed200));
    }

    [Fact]
    public void Unknown_model_of_a_subscribed_event_is_not_readable()
    {
        var route = SchemaRouting.Resolve(DayClosed200, [DayClosed100, DayClosed101]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.UnknownModel, null));
    }

    [Fact]
    public void Event_without_any_handler_is_not_subscribed()
    {
        var route = SchemaRouting.Resolve(DayClosed100, [TestEvent100]);

        route.ShouldBe(new SchemaRoute(SchemaMatch.NotSubscribed, null));
    }
}
