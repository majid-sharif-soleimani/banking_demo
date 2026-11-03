using System.Text;
using System.Text.Json;
using Bank.Agent.Eval;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.UnitTests;

public class EvalTests
{
    private static readonly EvalCase Transfer = new("t", "customer", ["Move 500"], "why",
        ToolsCalled: ["transfer_create"], ToolsNotCalled: ["loan_application_submit"], Shown: ["show_confirmation"],
        AnswerMatches: ["press confirm"], AnswerDoesNotMatch: ["has been transferred"], Language: "en");

    [Fact]
    public void A_run_that_follows_every_rule_passes()
    {
        var run = new EvalRun(["accounts_list", "transfer_create"], ["show_confirmation"], "Please check the values and press Confirm.");

        Grader.Check(Transfer, run).ShouldBeEmpty();
    }

    [Fact]
    public void Each_broken_rule_is_one_failure()
    {
        var run = new EvalRun(["loan_application_submit"], [], "Pengarna har överförts. 500 SEK has been transferred.");

        Grader.Check(Transfer, run).ShouldBe([
            "did not call transfer_create", "called loan_application_submit", "did not show show_confirmation",
            "answer lacks /press confirm/", "answer has /has been transferred/", "answer is in sv, not en"]);
    }

    [Theory]
    [InlineData("Your balance is 12 345.67 SEK.", "en")]
    [InlineData("Ditt saldo är 12 345,67 kr.", "sv")]
    [InlineData("موجودی حساب جاری شما 12,345.67 SEK است.", "fa")]
    public void The_language_guess_tells_the_three_languages_apart(string text, string language) =>
        Grader.LanguageOf(text).ShouldBe(language);

    [Fact]
    public void Every_tool_a_case_names_exists_for_its_role()
    {
        var cases = JsonSerializer.Deserialize<EvalCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cases.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        cases.Length.ShouldBeGreaterThanOrEqualTo(15);
        cases.Select(c => c.Id).ShouldBeUnique();
        foreach (var c in cases)
        {
            var principal = new Principal("x", c.Role == "admin" ? Role.Admin : Role.Customer, Guid.NewGuid(), "X");
            var session = new HostSession { Id = "x", BankToken = "x", Principal = principal, ChatId = Guid.NewGuid() };
            var names = AgentTools.For(FakeBank.For(c.Role), session, new UiTools(session), new EchoModel()).Select(t => t.Name).ToHashSet();
            foreach (var tool in (c.ToolsCalled ?? []).Concat(c.ToolsNotCalled ?? []))
            {
                names.ShouldContain(tool, $"case {c.Id}");
            }
        }
    }

    [Fact]
    public void The_test_payslip_is_a_pdf_with_a_correct_cross_reference()
    {
        var pdf = Encoding.ASCII.GetString(FakeBank.Payslip());

        pdf.ShouldStartWith("%PDF-1.4");
        var startxref = int.Parse(pdf.Split("startxref\n")[1].Split('\n')[0]);
        pdf[startxref..].ShouldStartWith("xref");
        pdf.ShouldContain("Net salary: 32 400 SEK");
    }

    [Fact]
    public async Task The_runner_records_tool_calls_and_shown_components_through_the_real_tools()
    {
        var model = new TwoStepModel("transfer_create", new Dictionary<string, object?>
        {
            ["fromAccountId"] = FakeBank.Current, ["toAccountId"] = FakeBank.Savings, ["amount"] = 500m, ["currency"] = "SEK",
        });
        var c = new EvalCase("t", "customer", ["Move 500 SEK to savings"], "why");

        var run = await new Runner(model, 1000).RunAsync(c, TestContext.Current.CancellationToken);

        run.ToolsCalled.ShouldBe(["transfer_create"]);
        run.Shown.ShouldBe(["show_confirmation"]);
        run.Answer.ShouldBe("Please press Confirm.");
    }

    /// <summary>First asks for one tool call, then answers with text.</summary>
    private sealed class TwoStepModel(string tool, Dictionary<string, object?> arguments) : IChatClient
    {
        private int calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(calls++ == 0
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", tool, arguments)]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Please press Confirm.")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class EchoModel : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
