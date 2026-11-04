using System.Net;
using System.Net.Http.Json;
using Bank.Customers.Api.Features;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Customers.IntegrationTests;

public class CustomerSearchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> RegisterAsync(CustomersApp app, string name, string email, string phone)
    {
        var response = await app.PostJsonAsync("/customers", new RegisterCustomer.Request(
            name, $"ID-{Guid.NewGuid():N}"[..20], new DateOnly(1990, 1, 1), email, phone, "Example Street 1", null), performedBy: "E001");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!.Id;
    }

    private static Task<HttpResponseMessage> SearchAsync(CustomersApp app, SearchCustomers.Request request, string? performedBy = "E001", Guid? customerId = null) =>
        app.Client(performedBy, customerId).PostAsJsonAsync("/customers/search", request, Ct);

    private static async Task<Page<CustomerSummary>> PageAsync(CustomersApp app, SearchCustomers.Request request)
    {
        var response = await SearchAsync(app, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page<CustomerSummary>>(Ct))!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;

    private static SearchCustomers.Request ByName(string name, int? limit = null, string? cursor = null) => new(name, null, null, null, limit, cursor);

    [Fact]
    public async Task Name_matches_any_part_case_insensitively_and_sorts_by_name()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var ada = await RegisterAsync(app, "Ada Lovelace", "ada@example.com", "+46 70 111 11 11");
        var byron = await RegisterAsync(app, "Byron Lovelace", "byron@example.com", "+46 70 222 22 22");
        await RegisterAsync(app, "Grace Hopper", "grace@example.com", "+46 70 333 33 33");

        var page = await PageAsync(app, ByName("LOVEL"));

        page.Items.Select(c => c.Id).ShouldBe([ada, byron]);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Email_phone_and_customer_number_match_exactly()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var ada = await RegisterAsync(app, "Ada Lovelace", "Ada@Example.com", "+46 70-111 11 11");
        await RegisterAsync(app, "Ada Byron", "ada.byron@example.com", "+46 70 111 11 12");

        (await PageAsync(app, new(null, " ada@example.COM ", null, null, null, null))).Items.Select(c => c.Id).ShouldBe([ada]);
        (await PageAsync(app, new(null, "ada@example", null, null, null, null))).Items.ShouldBeEmpty();
        (await PageAsync(app, new(null, null, "46701111111", null, null, null))).Items.Select(c => c.Id).ShouldBe([ada]);
        (await PageAsync(app, new(null, null, "70 111 11", null, null, null))).Items.ShouldBeEmpty();
        (await PageAsync(app, new(null, null, null, ada, null, null))).Items.ShouldHaveSingleItem().Name.ShouldBe("Ada Lovelace");
        (await PageAsync(app, new("Ada", null, "+46 70 111 11 12", null, null, null))).Items.ShouldHaveSingleItem().Name.ShouldBe("Ada Byron");
    }

    [Fact]
    public async Task National_id_matches_exactly_after_trimming()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var nationalId = $"NID-{Guid.NewGuid():N}"[..20];
        var response = await app.PostJsonAsync("/customers", new RegisterCustomer.Request(
            "Ada Lovelace", nationalId, new DateOnly(1990, 1, 1), "ada@example.com", "+46 70 111 11 11", "Example Street 1", null), performedBy: "E001");
        var ada = (await response.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!.Id;
        await RegisterAsync(app, "Ada Byron", "byron@example.com", "+46 70 111 11 12");

        (await PageAsync(app, new(null, null, null, null, null, null, $" {nationalId} "))).Items.Select(c => c.Id).ShouldBe([ada]);
        (await PageAsync(app, new(null, null, null, null, null, null, nationalId[..^1]))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Like_wildcards_in_the_term_match_literally()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        await RegisterAsync(app, "Ada Lovelace", "ada@example.com", "+46 70 111 11 11");
        var underscore = await RegisterAsync(app, "Ada_Lovelace", "ada2@example.com", "+46 70 111 11 12");

        (await PageAsync(app, ByName("a_l"))).Items.Select(c => c.Id).ShouldBe([underscore]);
        (await PageAsync(app, ByName("%%"))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pages_walk_every_match_once_including_customers_with_the_same_name()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var ids = new List<Guid>();
        foreach (var name in new[] { "Ann Berg", "Ann Berg", "Ann Berg", "Bo Berg", "Cia Berg" })
        {
            ids.Add(await RegisterAsync(app, name, $"{Guid.NewGuid():N}@example.com", "+46 70 000 00 00"));
        }

        var first = await PageAsync(app, ByName("berg", limit: 2));
        var second = await PageAsync(app, ByName("berg", limit: 2, cursor: first.NextCursor));
        var third = await PageAsync(app, ByName("berg", limit: 2, cursor: second.NextCursor));

        var seen = first.Items.Concat(second.Items).Concat(third.Items).ToList();
        seen.Select(c => c.Id).ShouldBe([.. ids[..3].Order(), ids[3], ids[4]]);
        seen.Select(c => c.Name).ShouldBe(["Ann Berg", "Ann Berg", "Ann Berg", "Bo Berg", "Cia Berg"]);
        third.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task A_customer_and_a_caller_without_an_actor_are_refused()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        var ada = await RegisterAsync(app, "Ada Lovelace", "ada@example.com", "+46 70 111 11 11");

        var customer = await SearchAsync(app, ByName("Ada"), performedBy: null, customerId: ada);
        var anonymous = await SearchAsync(app, ByName("Ada"), performedBy: null);

        customer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(customer)).ShouldBe("staff_only");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(anonymous)).ShouldBe("missing_actor");
    }

    [Fact]
    public async Task No_filter_lists_every_customer_page_by_page_with_the_national_id()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        await RegisterAsync(app, "Ada Lovelace", "ada@example.com", "+46 70 111 11 11");
        await RegisterAsync(app, "Grace Hopper", "grace@example.com", "+46 70 222 22 22");
        await RegisterAsync(app, "Alan Turing", "alan@example.com", "+46 70 333 33 33");

        var first = await PageAsync(app, new(null, null, null, null, 2, null));
        var second = await PageAsync(app, new(null, null, null, null, 2, first.NextCursor));

        first.Items.Select(c => c.Name).ShouldBe(["Ada Lovelace", "Alan Turing"]);
        second.Items.Select(c => c.Name).ShouldBe(["Grace Hopper"]);
        second.NextCursor.ShouldBeNull();
        first.Items.ShouldAllBe(c => c.NationalId.StartsWith("ID-"));
    }

    [Fact]
    public async Task Listing_every_customer_is_for_staff_only()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var asCustomer = await SearchAsync(app, new(null, null, null, null, null, null), performedBy: null, customerId: Guid.NewGuid());

        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Blank_or_too_short_filters_and_bad_paging_are_bad_requests()
    {
        await using var app = await CustomersApp.StartAsync(postgres);

        var blank = await SearchAsync(app, new(null, null, null, null, null, null, NationalId: " "));
        var shortName = await SearchAsync(app, ByName("A"));
        var limit = await SearchAsync(app, ByName("Ada", limit: 0));
        var cursor = await SearchAsync(app, ByName("Ada", cursor: "eyJuYW1lIjoiQWRhIn0"));

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await blank.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!.Errors.Keys.ShouldBe(["filter"]);
        (await shortName.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!.Errors.Keys.ShouldBe(["name"]);
        (await CodeAsync(limit)).ShouldBe("invalid_limit");
        (await CodeAsync(cursor)).ShouldBe("invalid_cursor");
    }

    [Fact]
    public async Task No_search_value_reaches_the_logs()
    {
        await using var app = await CustomersApp.StartAsync(postgres);
        await RegisterAsync(app, "Ada Lovelace", "ada@example.com", "+46 70 111 11 11");
        var before = app.LogLines.Count;

        (await PageAsync(app, new("Lovelace", "ada@example.com", "+46701111111", null, 1, null))).Items.ShouldHaveSingleItem();

        var lines = app.LogLines.Skip(before).ToList();
        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(l => !l.Contains("Lovelace") && !l.Contains("ada@example.com") && !l.Contains("46701111111"));
    }
}
