using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Customers.Api.Features.Appointments;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.Api.Features.Interactions;
using Bank.Customers.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Customers.IntegrationTests;

public class ReviewSupportTests(PostgresFixture postgres)
{
    private static readonly DateOnly Tomorrow = CustomersApp.Today.AddDays(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(CustomersApp App, CustomerResponse Customer)> StartWithCustomerAsync(PostgresFixture postgres)
    {
        var app = await CustomersApp.StartAsync(postgres);
        var customer = await (await app.PostJsonAsync("/customers", CustomerTests.NewCustomer())).Content.ReadFromJsonAsync<CustomerResponse>(Ct);
        return (app, customer!);
    }

    private static async Task<Guid> SubmittedApplicationAsync(CustomersApp app, Guid customerId)
    {
        var applicationId = Guid.NewGuid();
        await app.DeliverAsync(new LoanApplicationSubmitted(Guid.NewGuid(), CustomersApp.Today, "customer", applicationId, customerId, "CAR",
            new Money(200_000m, "SEK"), 36), applicationId.ToString());
        return applicationId;
    }

    private static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;

    [Fact]
    public async Task An_upload_is_stored_and_can_be_downloaded_again()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var bytes = "%PDF-1.7 payslip"u8.ToArray();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Upload"), "kind" },
            { new StringContent((await SubmittedApplicationAsync(app, customer.Id)).ToString()), "applicationId" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "payslip.pdf");

        var response = await app.CreateClient().PostAsync($"/customers/{customer.Id}/interactions", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var interaction = (await response.Content.ReadFromJsonAsync<InteractionResponse>(Ct))!;
        interaction.ShouldSatisfyAllConditions(i => i.FileName.ShouldBe("payslip.pdf"), i => i.FileSize.ShouldBe(bytes.Length), i => i.Kind.ShouldBe("Upload"));
        var download = await app.CreateClient().GetAsync($"/customers/{customer.Id}/interactions/{interaction.Id}/file", Ct);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task Notes_need_text_and_the_kind_must_be_known()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;

        var empty = await app.CreateClient().PostAsync($"/customers/{customer.Id}/interactions",
            new MultipartFormDataContent { { new StringContent("Note"), "kind" } }, Ct);
        var unknown = await app.CreateClient().PostAsync($"/customers/{customer.Id}/interactions",
            new MultipartFormDataContent { { new StringContent("Fax"), "kind" }, { new StringContent("x"), "content" } }, Ct);

        (await Code(empty)).ShouldBe("missing_content");
        (await Code(unknown)).ShouldBe("invalid_kind");
    }

    [Fact]
    public async Task InformationRequested_opens_a_request_and_emails_and_completing_it_publishes_InformationReceived_once()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var applicationId = Guid.NewGuid();
        var requested = new InformationRequested(Guid.NewGuid(), CustomersApp.Today, "E001", Guid.NewGuid(), applicationId, customer.Id, "Payslip\nID copy");

        await app.DeliverAsync(requested, applicationId.ToString());

        var request = (await app.GetJsonAsync<InformationRequestResponse[]>($"/information-requests?applicationId={applicationId}")).ShouldHaveSingleItem();
        request.Items.ShouldBe(["Payslip", "ID copy"]);
        app.Emails.Sent.ShouldHaveSingleItem().Body.ShouldContain("- ID copy");

        await app.CreateClient().PostAsync($"/information-requests/{request.Id}/complete", null, Ct);
        var again = await app.CreateClient().PostAsync($"/information-requests/{request.Id}/complete", null, Ct);

        (await again.Content.ReadFromJsonAsync<InformationRequestResponse>(Ct))!.Status.ShouldBe("Completed");
        await app.OutboxDrainedAsync();
        app.Published<InformationReceived>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.InformationRequestId.ShouldBe(requested.InformationRequestId), e => e.ApplicationId.ShouldBe(applicationId));
    }

    [Fact]
    public async Task Without_an_application_staff_get_the_open_requests_oldest_first_and_customers_get_403()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var older = new InformationRequested(Guid.NewGuid(), CustomersApp.Today, "E001", Guid.NewGuid(), Guid.NewGuid(), customer.Id, "Payslip");
        var newer = new InformationRequested(Guid.NewGuid(), Tomorrow, "E001", Guid.NewGuid(), Guid.NewGuid(), customer.Id, "ID copy");
        var done = new InformationRequested(Guid.NewGuid(), CustomersApp.Today, "E001", Guid.NewGuid(), Guid.NewGuid(), customer.Id, "Contract");
        await app.DeliverAsync(newer, newer.ApplicationId.ToString());
        await app.DeliverAsync(older, older.ApplicationId.ToString());
        await app.DeliverAsync(done, done.ApplicationId.ToString());
        await app.Client(performedBy: "E001").PostAsync($"/information-requests/{done.InformationRequestId}/complete", null, Ct);

        var queue = await app.Client(performedBy: "E001").GetFromJsonAsync<InformationRequestResponse[]>("/information-requests", Ct);
        var asCustomer = await app.Client(customerId: customer.Id).GetAsync("/information-requests", Ct);
        var anonymous = await app.CreateClient().GetAsync("/information-requests", Ct);

        queue!.Select(r => r.Id).ShouldBe([older.InformationRequestId, newer.InformationRequestId]);
        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Application_events_are_recorded_once_in_the_interaction_history()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var applicationId = Guid.NewGuid();
        var changed = new LoanApplicationStatusChanged(Guid.NewGuid(), CustomersApp.Today, "E001", applicationId, customer.Id, "UnderReview", "Rejected", "Income too low");

        await app.DeliverAsync(new LoanApplicationSubmitted(Guid.NewGuid(), CustomersApp.Today, "customer", applicationId, customer.Id, "CAR", new Money(200_000m, "SEK"), 36), "k");
        await app.DeliverAsync(changed, "k");
        await app.DeliverAsync(changed, "k");

        var history = await app.GetJsonAsync<InteractionResponse[]>($"/customers/{customer.Id}/interactions");
        history.Select(h => h.Content).ShouldBe(["Applied for CAR: 200000 SEK over 36 months.", "Application status UnderReview → Rejected: Income too low"]);
    }

    [Fact]
    public async Task Loan_events_send_their_emails_and_a_return_to_Current_sends_none()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var loanId = Guid.NewGuid();

        await app.DeliverAsync(new PaymentFailed(Guid.NewGuid(), CustomersApp.Today, "system", loanId, Guid.NewGuid(), customer.Id, 1, "Installment",
            new Money(6_170.85m, "SEK"), "insufficient_funds"), "k");
        await app.DeliverAsync(new LoanClassificationChanged(Guid.NewGuid(), CustomersApp.Today, "system", loanId, customer.Id, "Current", "Overdue", 1), "k");
        await app.DeliverAsync(new LoanClassificationChanged(Guid.NewGuid(), CustomersApp.Today, "system", loanId, customer.Id, "Overdue", "Current", 0), "k");
        await app.DeliverAsync(new LoanPaidOffEarly(Guid.NewGuid(), CustomersApp.Today, "system", loanId, customer.Id, new Money(197_514.49m, "SEK"), new Money(18_465.20m, "SEK")), "k");

        await app.DeliverAsync(new CardPaymentDeclined(Guid.NewGuid(), CustomersApp.Today, "system", Guid.NewGuid(), loanId, customer.Id, new Money(500m, "SEK"), "insufficient_funds"), "k");

        app.Emails.Sent.Select(e => e.Subject).ShouldBe(
            ["A loan payment could not be collected", "Your loan payments are late", "Your loan is paid off", "Your card payment was declined"]);
        app.Emails.Sent.Last().Body.ShouldContain("did not have enough money");
    }

    [Fact]
    public async Task Appointments_offer_free_slots_and_refuse_double_booking()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var slots = await app.GetJsonAsync<SlotResponse[]>($"/appointments/availability?date={Tomorrow:yyyy-MM-dd}");
        slots.Length.ShouldBe(35); // 7 slots × 5 employees
        var slot = slots.First(s => s.EmployeeId == "E002");
        var booking = new AppointmentEndpoints.BookRequest(await SubmittedApplicationAsync(app, customer.Id), customer.Id, slot.EmployeeId, slot.StartsAt);

        var first = await app.PostJsonAsync("/appointments", booking);
        var second = await app.PostJsonAsync("/appointments", booking with { ApplicationId = await SubmittedApplicationAsync(app, customer.Id) });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(second)).ShouldBe("slot_taken");
        (await app.GetJsonAsync<SlotResponse[]>($"/appointments/availability?date={Tomorrow:yyyy-MM-dd}")).Length.ShouldBe(34);
        await Eventually.AssertAsync(() =>
        {
            app.Published<AppointmentScheduled>().ShouldHaveSingleItem().EmployeeId.ShouldBe("E002");
            return Task.CompletedTask;
        });

        var appointment = (await first.Content.ReadFromJsonAsync<AppointmentResponse>(Ct))!;
        (await app.CreateClient().DeleteAsync($"/appointments/{appointment.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.PostJsonAsync("/appointments", booking with { ApplicationId = await SubmittedApplicationAsync(app, customer.Id) })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_customer_lists_their_own_appointments_and_none_of_another_customers()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var slot = (await app.GetJsonAsync<SlotResponse[]>($"/appointments/availability?date={Tomorrow:yyyy-MM-dd}")).First();
        var booked = await app.PostJsonAsync("/appointments",
            new AppointmentEndpoints.BookRequest(await SubmittedApplicationAsync(app, customer.Id), customer.Id, slot.EmployeeId, slot.StartsAt));
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentResponse>(Ct))!;

        var own = await app.Client(customerId: customer.Id).GetFromJsonAsync<AppointmentResponse[]>($"/appointments?customerId={customer.Id}", Ct);
        var other = await app.Client(customerId: Guid.NewGuid()).GetFromJsonAsync<AppointmentResponse[]>($"/appointments?customerId={customer.Id}", Ct);

        own!.ShouldHaveSingleItem().Id.ShouldBe(appointment.Id);
        other.ShouldBeEmpty();
    }

    [Fact]
    public async Task Slots_must_be_full_hours_on_a_later_day()
    {
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var application = await SubmittedApplicationAsync(app, customer.Id);
        var today10 = new DateTimeOffset(CustomersApp.Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(1));
        var tomorrow1030 = today10.AddDays(1).AddMinutes(30);

        (await Code(await app.PostJsonAsync("/appointments", new AppointmentEndpoints.BookRequest(application, customer.Id, "E001", today10)))).ShouldBe("invalid_slot");
        (await Code(await app.PostJsonAsync("/appointments", new AppointmentEndpoints.BookRequest(application, customer.Id, "E001", tomorrow1030)))).ShouldBe("invalid_slot");
        (await app.CreateClient().GetAsync($"/appointments/availability?date={CustomersApp.Today:yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_customer_cannot_book_or_attach_to_another_customers_application()
    {
        // Review of 2026-10-31: the application must belong to the customer named in the request.
        var (app, customer) = await StartWithCustomerAsync(postgres);
        await using var _ = app;
        var someoneElses = await SubmittedApplicationAsync(app, Guid.NewGuid());
        var slot = (await app.GetJsonAsync<SlotResponse[]>($"/appointments/availability?date={Tomorrow:yyyy-MM-dd}"))[0];

        var booking = await app.PostJsonAsync("/appointments", new AppointmentEndpoints.BookRequest(someoneElses, customer.Id, slot.EmployeeId, slot.StartsAt));
        var note = await app.CreateClient().PostAsync($"/customers/{customer.Id}/interactions", new MultipartFormDataContent
            { { new StringContent("Note"), "kind" }, { new StringContent("hello"), "content" }, { new StringContent(someoneElses.ToString()), "applicationId" } }, Ct);

        booking.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(note)).ShouldBe("application_not_found");
        app.Published<AppointmentScheduled>().ShouldBeEmpty();
    }
}
