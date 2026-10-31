using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Customers.Api.Email;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.IntegrationTests.Support;
using Bank.Messaging.Inbox;
using Bank.Testing;

namespace Bank.Customers.IntegrationTests;

public class NotificationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StandingOrderFailed Failed(Guid customerId) => new(
        Guid.NewGuid(), CustomersApp.Today, "system", Guid.NewGuid(), customerId, Guid.NewGuid(), new Money(1_500m, "SEK"),
        CustomersApp.Today, "insufficient_funds");

    [Fact]
    public async Task StandingOrderFailed_sends_one_email_and_records_the_notification()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var customer = await (await app.PostJsonAsync("/customers", CustomerTests.NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);
        var failed = Failed(customer!.Id);

        await app.DeliverAsync(failed, failed.StandingOrderId.ToString());

        var email = app.Emails.Sent.ShouldHaveSingleItem();
        email.To.ShouldBe("ada@example.com");
        email.Body.ShouldContain("1,500.00 SEK");
        (await app.ScalarAsync<string>("SELECT template FROM notification")).ShouldBe("standing_order_failed");
        (await app.ScalarAsync<Guid>("SELECT source_event_id FROM notification")).ShouldBe(failed.EventId);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_sends_one_email()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var customer = await (await app.PostJsonAsync("/customers", CustomerTests.NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);
        var failed = Failed(customer!.Id);

        (await app.DeliverAsync(failed, "k")).ShouldBe(ProcessOutcome.Handled);
        (await app.DeliverAsync(failed, "k")).ShouldBe(ProcessOutcome.Duplicate);

        app.Emails.Sent.Count.ShouldBe(1);
        (await app.ScalarAsync<long>("SELECT count(*) FROM notification")).ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_for_an_unknown_customer_throws_and_records_nothing()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        await Should.ThrowAsync<InvalidOperationException>(() => app.DeliverAsync(Failed(Guid.NewGuid()), "k"));

        app.Emails.Sent.ShouldBeEmpty();
        (await app.ScalarAsync<long>($"SELECT count(*) FROM inbox_message WHERE schema_uri = '{StandingOrderFailed.SchemaUri}'")).ShouldBe(0);
    }

    [Theory]
    [InlineData("Endpoint=smtp://localhost:1025", "localhost", 1025)]
    [InlineData("smtp://mailpit:25", "mailpit", 25)]
    public void The_Mailpit_connection_string_is_parsed(string value, string host, int port) =>
        SmtpEndpoint.Parse(value).ShouldBe(new SmtpEndpoint(host, port));

    [Theory]
    [InlineData("Endpoint=http://localhost:8025")]
    [InlineData("localhost")]
    public void A_malformed_Mailpit_connection_string_fails_with_a_hint(string value) =>
        Should.Throw<FormatException>(() => SmtpEndpoint.Parse(value)).Message.ShouldContain("Endpoint=smtp://host:port");
}
