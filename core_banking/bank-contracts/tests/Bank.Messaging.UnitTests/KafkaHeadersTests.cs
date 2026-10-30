using System.Text;
using Bank.Messaging.Kafka;
using Bank.Messaging.Transport;
using Confluent.Kafka;

namespace Bank.Messaging.UnitTests;

public class KafkaHeadersTests
{
    private static readonly TransportMessage Sample = new(
        Guid.Parse("6f1c2b8e-3c55-4d3a-9a11-0d6c1f6e2a10"),
        "iglu:se.bank/day_closed/jsonschema/1-0-0",
        "bank",
        """{"eventId":"6f1c2b8e-3c55-4d3a-9a11-0d6c1f6e2a10","date":"2026-10-30"}""",
        "system",
        new DateOnly(2026, 10, 30));

    [Fact]
    public void A_message_survives_the_round_trip_through_kafka_bytes()
    {
        var kafka = KafkaHeaders.ToKafka(Sample);

        KafkaHeaders.FromKafka(kafka.Key, kafka.Value, kafka.Headers).ShouldBe(Sample);
    }

    [Fact]
    public void Key_and_value_are_utf8_and_headers_carry_the_metadata()
    {
        var kafka = KafkaHeaders.ToKafka(Sample);

        Encoding.UTF8.GetString(kafka.Key).ShouldBe("bank");
        Encoding.UTF8.GetString(kafka.Value).ShouldBe(Sample.Payload);
        Header(kafka.Headers, "message-id").ShouldBe("6f1c2b8e-3c55-4d3a-9a11-0d6c1f6e2a10");
        Header(kafka.Headers, "schema-uri").ShouldBe("iglu:se.bank/day_closed/jsonschema/1-0-0");
        Header(kafka.Headers, "performed-by").ShouldBe("system");
        Header(kafka.Headers, "occurred-on").ShouldBe("2026-10-30");
    }

    [Theory]
    [InlineData("message-id")]
    [InlineData("schema-uri")]
    [InlineData("performed-by")]
    [InlineData("occurred-on")]
    public void A_missing_header_makes_the_message_invalid(string header)
    {
        var kafka = KafkaHeaders.ToKafka(Sample);
        kafka.Headers.Remove(header);

        var error = Should.Throw<InvalidMessageException>(() => KafkaHeaders.FromKafka(kafka.Key, kafka.Value, kafka.Headers));

        error.Message.ShouldContain(header);
    }

    [Theory]
    [InlineData("message-id", "not-a-guid")]
    [InlineData("schema-uri", "day_closed")]
    [InlineData("occurred-on", "30/10/2026")]
    [InlineData("performed-by", " ")]
    public void A_malformed_header_makes_the_message_invalid(string header, string value)
    {
        var kafka = KafkaHeaders.ToKafka(Sample);
        kafka.Headers.Remove(header);
        kafka.Headers.Add(header, Encoding.UTF8.GetBytes(value));

        Should.Throw<InvalidMessageException>(() => KafkaHeaders.FromKafka(kafka.Key, kafka.Value, kafka.Headers))
            .Message.ShouldContain(header);
    }

    [Fact]
    public void A_message_without_key_or_value_is_invalid()
    {
        var kafka = KafkaHeaders.ToKafka(Sample);

        Should.Throw<InvalidMessageException>(() => KafkaHeaders.FromKafka(null, kafka.Value, kafka.Headers));
        Should.Throw<InvalidMessageException>(() => KafkaHeaders.FromKafka(kafka.Key, null, kafka.Headers));
    }

    private static string Header(Headers headers, string name) => Encoding.UTF8.GetString(headers.GetLastBytes(name));
}
