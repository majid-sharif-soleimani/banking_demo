using System.Net;
using System.Net.Http.Json;
using Bank.Contracts.Events;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Customers.IntegrationTests;

public class CustomerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static RegisterCustomer.Request NewCustomer(string nationalId = "19900101-1234", string? language = null) => new(
        "Ada Lovelace", nationalId, new DateOnly(1990, 1, 1), "ada@example.com", "+46 70 000 00 00", "Example Street 1", language);

    [Fact]
    public async Task Register_creates_the_customer_and_publishes_CustomerRegistered()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var response = await app.PostJsonAsync("/customers", NewCustomer(language: "sv"), performedBy: "E001");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var customer = (await response.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;
        response.Headers.Location!.ToString().ShouldBe($"/customers/{customer.Id}");
        customer.PreferredLanguage.ShouldBe("sv");
        customer.Status.ShouldBe("Active");
        await Eventually.AssertAsync(() =>
        {
            var registered = app.Published<CustomerRegistered>().ShouldHaveSingleItem();
            registered.ShouldSatisfyAllConditions(
                e => e.CustomerId.ShouldBe(customer.Id),
                e => e.Name.ShouldBe("Ada Lovelace"),
                e => e.PreferredLanguage.ShouldBe("sv"),
                e => e.OccurredOn.ShouldBe(CustomersApp.Today),
                e => e.PerformedBy.ShouldBe("E001"));
            return Task.CompletedTask;
        });
        app.Broker.Sent.Single(m => m.SchemaUri == CustomerRegistered.SchemaUri).Key.ShouldBe(customer.Id.ToString());
    }

    [Fact]
    public async Task The_preferred_language_defaults_to_English()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var customer = await (await app.PostJsonAsync("/customers", NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);

        customer!.PreferredLanguage.ShouldBe("en");
    }

    [Fact]
    public async Task Invalid_input_returns_a_validation_problem_and_publishes_nothing()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var response = await app.PostJsonAsync("/customers", NewCustomer() with { Email = "nope", Name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!;
        problem.Errors.Keys.ShouldBe(["name", "email"], ignoreOrder: true);
        (await app.ScalarAsync<long>("SELECT count(*) FROM customer")).ShouldBe(0);
    }

    [Fact]
    public async Task A_national_id_can_be_registered_only_once()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        await app.PostJsonAsync("/customers", NewCustomer());

        var again = await app.PostJsonAsync("/customers", NewCustomer());

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("national_id_taken");
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_national_id_create_one_customer()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => app.PostJsonAsync("/customers", NewCustomer())));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
        await app.OutboxDrainedAsync();
        app.Published<CustomerRegistered>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Get_returns_the_customer_or_404()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var created = await (await app.PostJsonAsync("/customers", NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);

        (await app.GetJsonAsync<CustomerResponse>($"/customers/{created!.Id}")).NationalId.ShouldBe("19900101-1234");
        (await app.CreateClient().GetAsync($"/customers/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_call_for_another_customer_gets_404()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var created = await (await app.PostJsonAsync("/customers", NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);

        (await app.Client(customerId: Guid.NewGuid()).GetAsync($"/customers/{created!.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Client(customerId: created.Id).GetAsync($"/customers/{created.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_changes_only_the_contact_details_given()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var created = await (await app.PostJsonAsync("/customers", NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);

        var response = await app.Client(performedBy: "E002").PatchAsJsonAsync(
            $"/customers/{created!.Id}", new UpdateContactDetails.Request("new@example.com", null, null, "fa"), Ct);

        var updated = (await response.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;
        updated.Email.ShouldBe("new@example.com");
        updated.PreferredLanguage.ShouldBe("fa");
        updated.Phone.ShouldBe(created.Phone);
        updated.Name.ShouldBe(created.Name);
        (await app.ScalarAsync<string>($"SELECT performed_by FROM customer WHERE id = '{created.Id}'")).ShouldBe("E002");
    }

    [Fact]
    public async Task Patch_with_an_invalid_email_is_rejected()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var created = await (await app.PostJsonAsync("/customers", NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);

        var response = await app.CreateClient().PatchAsJsonAsync($"/customers/{created!.Id}", new UpdateContactDetails.Request("bad", null, null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
