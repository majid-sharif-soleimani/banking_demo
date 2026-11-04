using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.IntegrationTests.Support;
using Bank.Testing;
using Npgsql;
using static Bank.Agent.IntegrationTests.Support.ScriptedModel;

namespace Bank.Agent.IntegrationTests;

/// <summary>
/// Phase 2, stage 3: what the person does outside the text box. A change runs only after Confirm, a form's values are
/// checked by the host, an upload goes to the bank, and old chats are read-only and per person.
/// </summary>
public class ActionTests(PostgresFixture postgres)
{
    private static readonly Guid From = Guid.Parse("0199a000-0000-7000-8000-000000000001");

    private static readonly Guid To = Guid.Parse("0199a000-0000-7000-8000-000000000002");

    private static readonly object TransferArgs = new { fromAccountId = From, toAccountId = To, amount = 250.00m, currency = "SEK" };

    private static readonly object AmountForm = new
    {
        title = "Transfer",
        fields = new[] { new { name = "amount", label = "Amount (SEK)", type = "number", required = true, options = (string[]?)null, min = 1m, max = 1000m } },
        submitLabel = "Continue",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement ToolArgs(List<JsonElement> events, string tool)
    {
        var start = events.Single(e => e.GetProperty("type").GetString() == "TOOL_CALL_START" && e.GetProperty("toolCallName").GetString() == tool);
        var id = start.GetProperty("toolCallId").GetString();
        var args = events.Single(e => e.GetProperty("type").GetString() == "TOOL_CALL_ARGS" && e.GetProperty("toolCallId").GetString() == id);
        return JsonDocument.Parse(args.GetProperty("delta").GetString()!).RootElement;
    }

    private static async Task<List<(string Author, string Kind, string Content)>> Messages(AgentApp app, Guid chatId)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT author, kind, content FROM chat_message WHERE chat_id = @id ORDER BY sequence", connection);
        command.Parameters.AddWithValue("id", chatId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(string, string, string)>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<(AgentApp App, Browser Browser, Guid ChatId, string ChangeId)> AskForTransferAsync(PostgresFixture postgres)
    {
        var model = new ScriptedModel(new CallTool("transfer_create", TransferArgs), new Answer("Please press Confirm."), new Answer("Done."));
        var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();
        var events = await browser.RunAsync("Move 250 SEK to my savings");
        return (app, browser, chatId, ToolArgs(events, "show_confirmation").GetProperty("changeId").GetString()!);
    }

    [Fact]
    public async Task A_change_the_model_asks_for_is_shown_for_confirmation_and_nothing_reaches_the_bank()
    {
        var (app, _, chatId, changeId) = await AskForTransferAsync(postgres);
        await using var _ = app;

        app.BankTools.Ran.ShouldBeEmpty();
        changeId.ShouldNotBeNullOrEmpty();
        var stored = await Messages(app, chatId);
        var card = JsonDocument.Parse(stored.Single(m => m.Kind == "confirmation").Content).RootElement;
        card.GetProperty("title").GetString().ShouldBe("Transfer money");
        card.GetProperty("fields").EnumerateArray().Select(f => (f.GetProperty("label").GetString(), f.GetProperty("value").GetString()))
            .ShouldContain(("Amount", "250.00"));
    }

    [Fact]
    public async Task Confirm_runs_the_stored_change_once_with_the_confirmation_as_idempotency_key()
    {
        var (app, browser, chatId, changeId) = await AskForTransferAsync(postgres);
        await using var _ = app;

        using var confirmed = await browser.PostAsync($"/agent/changes/{changeId}/confirm");
        using var again = await browser.PostAsync($"/agent/changes/{changeId}/confirm");

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await confirmed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("done");
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var ran = app.BankTools.Ran.ShouldHaveSingleItem();
        ran.Tool.ShouldBe("transfer_create");
        ran.ConfirmationId.ShouldBe(changeId);
        ran.Arguments.ShouldContain(To.ToString());
        var result = (await Messages(app, chatId)).Last();
        (result.Author, result.Kind).ShouldBe(("user", "change_result"));
        result.Content.ShouldContain("\"status\":\"done\"");
        result.Content.ShouldContain("t-1");
        app.LogLines.ShouldContain(l => l.Contains("Change transfer_create in chat") && l.Contains("done"));
    }

    [Fact]
    public async Task A_change_whose_answer_was_lost_stays_pending_and_Confirm_again_reuses_the_same_key()
    {
        var (app, browser, chatId, changeId) = await AskForTransferAsync(postgres);
        await using var _ = app;
        app.BankTools.LoseNextAnswer = true;

        using var lost = await browser.PostAsync($"/agent/changes/{changeId}/confirm");
        using var retried = await browser.PostAsync($"/agent/changes/{changeId}/confirm");

        (await lost.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("unknown");
        (await retried.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("done");
        // Both calls carry the confirmation id, so the bank's idempotency check makes one transfer of them.
        app.BankTools.Ran.Select(r => r.ConfirmationId).ShouldBe([changeId, changeId]);
        var results = (await Messages(app, chatId)).Where(m => m.Kind == "change_result").Select(m => m.Content).ToList();
        results.Count.ShouldBe(2);
        results[0].ShouldContain("\"status\":\"unknown\"");
        results[1].ShouldContain("\"status\":\"done\"");
    }

    [Fact]
    public async Task The_model_sees_the_outcome_of_the_confirmation_in_the_next_turn()
    {
        var model = new ScriptedModel(new CallTool("transfer_create", TransferArgs), new Answer("Please press Confirm."), new Answer("Done."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);
        var changeId = ToolArgs(await browser.RunAsync("Move 250 SEK"), "show_confirmation").GetProperty("changeId").GetString();
        (await browser.PostAsync($"/agent/changes/{changeId}/confirm")).EnsureSuccessStatusCode();

        await browser.RunAsync("Confirmed.");

        var seen = model.Calls[^1].Messages;
        seen.ShouldContain(m => m.Text.StartsWith("(My change result:") && m.Text.Contains("t-1"));
        seen[^1].Text.ShouldBe("Confirmed.");
    }

    [Fact]
    public async Task Cancel_drops_the_change_and_records_it()
    {
        var (app, browser, chatId, changeId) = await AskForTransferAsync(postgres);
        await using var _ = app;

        (await browser.PostAsync($"/agent/changes/{changeId}/cancel")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.PostAsync($"/agent/changes/{changeId}/confirm")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        app.BankTools.Ran.ShouldBeEmpty();
        (await Messages(app, chatId)).Last().Content.ShouldContain("cancelled");
    }

    [Fact]
    public async Task Another_session_cannot_confirm_and_a_cross_site_post_is_refused()
    {
        var (app, browser, _, changeId) = await AskForTransferAsync(postgres);
        await using var _ = app;
        var other = app.NewBrowser();
        await other.LoginOkAsync("IMP-C001", "IMP-C001");

        using var foreign = await other.PostAsync($"/agent/changes/{changeId}/confirm");
        using var noHeader = await browser.PostAsync($"/agent/changes/{changeId}/confirm", withClientHeader: false);

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noHeader.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        app.BankTools.Ran.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_form_is_checked_by_the_host_and_its_values_reach_the_model()
    {
        var model = new ScriptedModel(new CallTool("show_form", AmountForm), new Answer("Waiting."), new Answer("Thanks."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();
        var formId = ToolArgs(await browser.RunAsync("I want to transfer"), "show_form").GetProperty("formId").GetString();

        using var tooMuch = await browser.PostAsync($"/agent/forms/{formId}", new { values = new { amount = "5000" } });
        using var right = await browser.PostAsync($"/agent/forms/{formId}", new { values = new { amount = "250" } });
        await browser.RunAsync("Sent the form.");

        tooMuch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMuch.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("amount").GetString().ShouldBe("At most 1000.");
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Messages(app, chatId)).ShouldContain(m => m.Kind == "form_values" && m.Content.Contains("\"amount\":\"250\""));
        model.Calls[^1].Messages.ShouldContain(m => m.Text.StartsWith("(My form values:") && m.Text.Contains("250"));
    }

    [Fact]
    public async Task A_card_payment_with_another_amount_than_the_form_gets_no_card_until_the_model_uses_the_persons_amount()
    {
        var model = new ScriptedModel(
            new CallTool("show_form", AmountForm), new Answer("Waiting."),
            new CallTool("card_payment", new { cardNumber = "test-card", loanId = From, amount = 8004.17m, currency = "SEK", scheduleItemNumber = 7 }),
            new CallTool("card_payment", new { cardNumber = "test-card", loanId = From, amount = 250.00m, currency = "SEK", scheduleItemNumber = 7 }),
            new Answer("Please press Confirm."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();
        var formId = ToolArgs(await browser.RunAsync("Pay part of my installment"), "show_form").GetProperty("formId").GetString();

        using var sent = await browser.PostAsync($"/agent/forms/{formId}", new { values = new { amount = "250" } });
        await browser.RunAsync("I sent the form.");

        var card = JsonDocument.Parse((await Messages(app, chatId)).Single(m => m.Kind == "confirmation").Content).RootElement;
        card.GetProperty("fields").EnumerateArray().Select(f => (f.GetProperty("label").GetString(), f.GetProperty("value").GetString()))
            .ShouldContain(("Amount", "250.00"));
        app.BankTools.Ran.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_requested_document_is_uploaded_to_the_bank_and_a_wrong_file_is_refused()
    {
        var application = Guid.NewGuid();
        var model = new ScriptedModel(new CallTool("request_document", new { applicationId = application, description = "Your payslip" }), new Answer("Waiting."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);
        var requestId = ToolArgs(await browser.RunAsync("Here is my application"), "request_document").GetProperty("requestId").GetString();

        using var wrong = await browser.PostFileAsync($"/agent/documents/{requestId}", "MZ"u8.ToArray(), "tool.exe", "application/x-msdownload");
        using var right = await browser.PostFileAsync($"/agent/documents/{requestId}", "%PDF-1.4"u8.ToArray(), "payslip.pdf", "application/pdf");

        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ran = app.BankTools.Ran.ShouldHaveSingleItem();
        (ran.Tool, ran.ConfirmationId).ShouldBe((McpBankTools.DocumentUpload, requestId));
        ran.Arguments.ShouldContain(application.ToString());
        ran.Arguments.ShouldContain("payslip.pdf");
    }

    [Fact]
    public async Task A_customer_lists_and_reads_only_their_own_chats_and_the_admin_reads_all_with_a_log_line()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var tess = app.NewBrowser();
        var tessChat = (await tess.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();
        await tess.RunAsync("What are my balances?");
        var anna = app.NewBrowser();
        var annaChat = (await anna.LoginOkAsync("IMP-C001", "IMP-C001")).GetProperty("chatId").GetGuid();
        var admin = app.NewBrowser();
        await admin.LoginOkAsync("admin", "admin");

        var tessList = await (await tess.GetAsync("/chats")).Content.ReadFromJsonAsync<JsonElement>(Ct);
        using var tessReadsAnna = await tess.GetAsync($"/chats/{annaChat}");
        var adminList = await (await admin.GetAsync("/chats")).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var adminReadsTess = await (await admin.GetAsync($"/chats/{tessChat}")).Content.ReadFromJsonAsync<JsonElement>(Ct);

        tessList.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldBe([tessChat]);
        tessList[0].GetProperty("firstQuestion").GetString().ShouldBe("What are my balances?");
        tessReadsAnna.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        adminList.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldContain(tessChat);
        adminList.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldContain(annaChat);
        adminReadsTess.GetProperty("messages").GetArrayLength().ShouldBe(2);
        app.LogLines.ShouldContain(l => l.Contains($"Chat {tessChat} read by admin"));
    }

    [Fact]
    public async Task An_admin_approval_waits_for_the_admins_click_and_runs_once_with_the_key()
    {
        var applicationId = Guid.NewGuid();
        var model = new ScriptedModel(new CallTool("loan_application_approve", new { applicationId }), new Answer("Please press Confirm."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var admin = app.NewBrowser();
        await admin.LoginOkAsync("admin", "admin");

        var events = await admin.RunAsync("Approve it");
        var card = ToolArgs(events, "show_confirmation");
        app.BankTools.Ran.ShouldBeEmpty();
        using var confirmed = await admin.PostAsync($"/agent/changes/{card.GetProperty("changeId").GetString()}/confirm");

        card.GetProperty("title").GetString().ShouldBe("Approve a loan application");
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ran = app.BankTools.Ran.ShouldHaveSingleItem();
        ran.Tool.ShouldBe("loan_application_approve");
        ran.ConfirmationId.ShouldBe(card.GetProperty("changeId").GetString());
        ran.Arguments.ShouldContain(applicationId.ToString());
        app.LogLines.ShouldContain(l => l.Contains("Change loan_application_approve in chat") && l.Contains("Admin"));
    }

    [Fact]
    public async Task A_chart_with_a_missing_value_is_refused()
    {
        var chart = new { title = "Spending", kind = "bar", labels = new[] { "Oct", "Nov" }, series = new[] { new { name = "SEK", values = new[] { 1m } } }, unit = "SEK" };
        var model = new ScriptedModel(new CallTool("show_chart", chart), new Answer("Sorry."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        var events = await browser.RunAsync("Chart please");

        events.ShouldNotContain(e => e.GetProperty("type").GetString() == "TOOL_CALL_START");
        model.EverythingSeen.ShouldContain("Every series needs one value per label (2).");
    }
}
