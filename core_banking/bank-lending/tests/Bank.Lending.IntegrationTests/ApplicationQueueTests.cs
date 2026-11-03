using System.Net;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Features;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class ApplicationQueueTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Page<ApplicationResponse>> PageAsync(LendingApp app, string query)
    {
        var response = await app.Client("staff-1").GetAsync($"/loan-applications{query}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page<ApplicationResponse>>(Ct))!;
    }

    private static async Task<List<Guid>> SubmitManyAsync(LendingApp app, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            ids.Add((await app.SubmitOkAsync()).Id);
        }

        return ids;
    }

    /// <summary>A second customer with a SEK account, as Customers and Accounts would announce them.</summary>
    private static async Task<Guid> SubmitForAnotherCustomerAsync(LendingApp app)
    {
        var customer = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();
        await app.DeliverAsync(new CustomerRegistered(Guid.NewGuid(), LendingApp.Today, "system", customer, "Grace Hopper", "en"), customer.ToString());
        await app.DeliverAsync(new AccountOpened(Guid.NewGuid(), LendingApp.Today, "system", account, "234567893", customer, "Current", "SEK"), account.ToString());
        var response = await app.PostJsonAsync("/loan-applications", new ApplicationEndpoints.SubmitRequest(
            customer, "CAR", new Money(100_000m, "SEK"), 24, account, "A car", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>(Ct))!.Id;
    }

    [Fact]
    public async Task Pages_walk_the_queue_oldest_first_without_gaps_or_repeats()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var submitted = await SubmitManyAsync(app, 5);

        var first = await PageAsync(app, "?limit=2");
        var second = await PageAsync(app, $"?limit=2&cursor={first.NextCursor}");
        var third = await PageAsync(app, $"?limit=2&cursor={second.NextCursor}");

        first.Items.Select(a => a.Id).ShouldBe(submitted[..2]);
        second.Items.Select(a => a.Id).ShouldBe(submitted[2..4]);
        third.Items.Select(a => a.Id).ShouldBe(submitted[4..]);
        third.NextCursor.ShouldBeNull();
        first.Items[0].History.ShouldHaveSingleItem().ToStatus.ShouldBe("Submitted");
    }

    [Fact]
    public async Task A_page_exactly_as_long_as_the_rest_is_the_last_page_and_an_empty_queue_is_one_empty_page()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var empty = await PageAsync(app, "");
        await SubmitManyAsync(app, 3);
        var exact = await PageAsync(app, "?limit=3");

        empty.Items.ShouldBeEmpty();
        empty.NextCursor.ShouldBeNull();
        exact.Items.Count.ShouldBe(3);
        exact.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task An_application_submitted_while_paging_comes_at_the_end_and_nothing_repeats()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var submitted = await SubmitManyAsync(app, 3);

        var first = await PageAsync(app, "?limit=2");
        var late = (await app.SubmitOkAsync()).Id;
        var rest = await PageAsync(app, $"?limit=2&cursor={first.NextCursor}");

        first.Items.Concat(rest.Items).Select(a => a.Id).ShouldBe([.. submitted, late]);
        rest.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task The_queue_filters_by_one_or_more_statuses_and_by_customer()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var ids = await SubmitManyAsync(app, 3);
        (await app.ActAsync(ids[1], "review")).EnsureSuccessStatusCode();
        (await app.ActAsync(ids[2], "cancel", new { reason = "Changed my mind" })).EnsureSuccessStatusCode();
        var other = await SubmitForAnotherCustomerAsync(app);

        (await PageAsync(app, "?status=UnderReview")).Items.Select(a => a.Id).ShouldBe([ids[1]]);
        (await PageAsync(app, "?status=submitted&status=UnderReview")).Items.Select(a => a.Id).ShouldBe([ids[0], ids[1], other]);
        (await PageAsync(app, $"?customerId={app.CustomerId}")).Items.Select(a => a.Id).ShouldBe(ids);
        (await PageAsync(app, $"?status=Submitted&customerId={app.CustomerId}")).Items.Select(a => a.Id).ShouldBe([ids[0]]);
    }

    [Fact]
    public async Task A_caller_without_an_actor_is_refused()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var anonymous = await app.CreateClient().GetAsync("/loan-applications", Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApplicationTests.Code(anonymous)).ShouldBe("missing_actor");
    }

    [Fact]
    public async Task A_customer_sees_only_their_own_applications_whatever_customer_id_they_send()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var own = (await app.SubmitOkAsync()).Id;
        await SubmitForAnotherCustomerAsync(app);
        var client = app.Client(customerId: app.CustomerId);

        var all = await client.GetFromJsonAsync<Page<ApplicationResponse>>("/loan-applications", Ct);
        var other = await client.GetFromJsonAsync<Page<ApplicationResponse>>($"/loan-applications?customerId={Guid.NewGuid()}", Ct);

        all!.Items.Select(a => a.Id).ShouldBe([own]);
        other!.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?limit=0", "invalid_limit")]
    [InlineData("?limit=201", "invalid_limit")]
    [InlineData("?cursor=not-a-cursor", "invalid_cursor")]
    [InlineData("?cursor=e30", "invalid_cursor")]
    [InlineData("?status=Pending", "invalid_status")]
    public async Task Bad_paging_or_filter_input_is_a_bad_request(string query, string code)
    {
        await using var app = await LendingApp.StartAsync(postgres);

        var response = await app.Client("staff-1").GetAsync($"/loan-applications{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApplicationTests.Code(response)).ShouldBe(code);
    }
}
