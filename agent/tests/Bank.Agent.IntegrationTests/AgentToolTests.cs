using System.Text.Json;
using Bank.Agent.IntegrationTests.Support;
using Bank.Testing;
using Npgsql;
using static Bank.Agent.IntegrationTests.Support.ScriptedModel;

namespace Bank.Agent.IntegrationTests;

/// <summary>
/// The agent loop with a scripted model (phase 2, stage 2): bank tools, UI tools, what is stored, what the browser
/// gets, and what the model never sees.
/// </summary>
public class AgentToolTests(PostgresFixture postgres)
{
    private static readonly object AccountsTable = new
    {
        title = "Your accounts",
        columns = new[] { new { label = "Account", type = "text" }, new { label = "Balance", type = "money" } },
        rows = new[] { new[] { "5555 0000 1", "1 200.00 SEK" } },
    };

    /// <summary>The UI tools every role gets; the upload tool never reaches the model.</summary>
    public static readonly string[] UiToolNames = ["request_document", "show_card", "show_chart", "show_diagram", "show_form", "show_table"];

    private static string[] Types(List<JsonElement> events) => [.. events.Select(e => e.GetProperty("type").GetString()!)];

    private static async Task<List<(string Author, string Kind, string Content)>> Messages(AgentApp app, Guid chatId)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT author, kind, content FROM chat_message WHERE chat_id = @id ORDER BY sequence", connection);
        command.Parameters.AddWithValue("id", chatId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    [Fact]
    public async Task A_bank_tool_and_a_table_reach_the_browser_as_one_table_and_text_and_are_stored_with_their_kind()
    {
        var model = new ScriptedModel(new CallTool("accounts_list", new { }), new CallTool("show_table", AccountsTable), new Answer("Savings has the most."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();

        var events = await browser.RunAsync("What are my balances?");

        Types(events).ShouldBe(["RUN_STARTED", "TOOL_CALL_START", "TOOL_CALL_ARGS", "TOOL_CALL_END", "TOOL_CALL_RESULT",
            "TEXT_MESSAGE_START", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_END", "RUN_FINISHED"]);
        // Only the UI tool reaches the browser; the bank call stays between the host and the bank.
        events.Where(e => e.GetProperty("type").GetString() == "TOOL_CALL_START").Select(e => e.GetProperty("toolCallName").GetString())
            .ShouldBe(["show_table"]);
        var table = JsonDocument.Parse(events.Single(e => e.GetProperty("type").GetString() == "TOOL_CALL_ARGS").GetProperty("delta").GetString()!).RootElement;
        table.GetProperty("rows")[0][1].GetString().ShouldBe("1 200.00 SEK");

        var stored = await Messages(app, chatId);
        stored.Select(m => (m.Author, m.Kind)).ShouldBe([("user", "text"), ("assistant", "table"), ("assistant", "text")]);
        stored[1].Content.ShouldContain("Your accounts");
        stored[2].Content.Trim().ShouldBe("Savings has the most.");
    }

    [Fact]
    public async Task The_bank_tools_act_with_the_session_token_and_customer_while_the_model_never_sees_the_token()
    {
        var model = new ScriptedModel(new CallTool("accounts_list", new { }), new Answer("You have one account."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        await browser.RunAsync("What are my balances?");

        var token = app.Gateway.IssuedTokens.Single();
        app.BankTools.Opened.ShouldBe([(token, StubGateway.TesterCustomerId)]);
        model.ToolNamesOfFirstCall.Order().ShouldBe([.. new[] { "accounts_list", "transfer_create" }.Concat(UiToolNames).Order()]);
        var seen = model.EverythingSeen;
        seen.ShouldContain("5555 0000 1");          // the tool result reached the model
        seen.ShouldContain("Tess Tester");          // the instructions name the person
        seen.ShouldNotContain(token);
        seen.ShouldNotContain(StubGateway.TesterPassword);
        seen.ShouldNotContain(StubGateway.TesterCustomerId.ToString());
    }

    [Fact]
    public async Task The_admin_gets_no_bank_tools_in_this_stage()
    {
        var model = new ScriptedModel(new Answer("Admin tools come later."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync("admin", "admin");

        await browser.RunAsync("Show the trial balance");

        app.BankTools.Opened.ShouldBeEmpty();
        model.ToolNamesOfFirstCall.Order().ShouldBe(UiToolNames.Order());
    }

    [Fact]
    public async Task A_table_with_a_missing_cell_is_not_shown_and_the_model_is_told_why()
    {
        var broken = new { title = "Accounts", columns = new[] { new { label = "A", type = "text" }, new { label = "B", type = "money" } }, rows = new[] { new[] { "only one" } } };
        var model = new ScriptedModel(new CallTool("show_table", broken), new Answer("Sorry."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        var events = await browser.RunAsync("Accounts?");

        Types(events).ShouldNotContain("TOOL_CALL_START");
        model.EverythingSeen.ShouldContain("Not shown: Row 1 has 1 cells");
    }

    [Fact]
    public async Task A_shown_table_is_part_of_the_history_the_model_sees_in_the_next_turn()
    {
        var model = new ScriptedModel(new CallTool("show_table", AccountsTable), new Answer("Here."), new Answer("The first one."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);
        await browser.RunAsync("Accounts?");

        await browser.RunAsync("Which one has the most?");

        var lastCall = model.Calls[^1].Messages;
        lastCall.ShouldContain(m => m.Text.StartsWith("(Shown to the person as a table:") && m.Text.Contains("5555 0000 1"));
        lastCall[^1].Text.ShouldBe("Which one has the most?");
    }

    [Fact]
    public async Task Every_tool_call_is_logged_with_its_name_and_outcome_but_without_its_data()
    {
        var model = new ScriptedModel(new CallTool("accounts_list", new { }), new Answer("Done."));
        await using var app = await AgentApp.StartAsync(postgres, model);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetString();

        await browser.RunAsync("Accounts?");

        app.LogLines.ShouldContain(l => l.Contains("Tool accounts_list in chat " + chatId) && l.Contains("ok"));
        app.LogLines.ShouldNotContain(l => l.Contains("5555 0000 1"));
    }

    [Fact]
    public async Task When_the_bank_tools_cannot_be_reached_the_run_fails_like_a_bank_outage()
    {
        await using var app = await AgentApp.StartAsync(postgres, new ScriptedModel(new Answer("never")));
        app.BankTools.Down = true;
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        var events = await browser.RunAsync("Accounts?");

        events[^1].GetProperty("type").GetString().ShouldBe("RUN_ERROR");
        events[^1].GetProperty("code").GetString().ShouldBe("bank_unavailable");
    }

    [Fact]
    public async Task When_the_model_fails_the_run_ends_with_an_error_and_the_chat_stays_open()
    {
        await using var app = await AgentApp.StartAsync(postgres, new ScriptedModel(new Fail(), new Answer("Back again.")));
        var browser = app.NewBrowser();
        await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        var failed = await browser.RunAsync("Accounts?");
        var next = await browser.RunAsync("Hello?");

        failed[^1].GetProperty("code").GetString().ShouldBe("model_unavailable");
        next[^1].GetProperty("type").GetString().ShouldBe("RUN_FINISHED");
    }
}
