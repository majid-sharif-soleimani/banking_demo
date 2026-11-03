using System.Runtime.CompilerServices;
using System.Text.Json;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Auth;
using Bank.Agent.Host.Logic;
using Microsoft.Extensions.AI;

namespace Bank.Agent.IntegrationTests.Support;

/// <summary>
/// Plays the language model with a fixed script, so a test knows exactly which tools the "model" calls. Each call of
/// the model takes the next step: a tool call, or a final text. It records everything it was sent, so a test can check
/// what the model saw (instructions, history, tool results, tool list) and what it never saw (the bank token).
/// </summary>
public sealed class ScriptedModel(params ScriptedModel.Step[] steps) : IChatClient
{
    public abstract record Step;

    public sealed record CallTool(string Name, object Arguments) : Step;

    public sealed record Answer(string Text) : Step;

    public sealed record Fail : Step;

    private readonly Queue<Step> script = new(steps);

    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    /// <summary>Everything the model was sent, as one text: instructions, messages, tool names and tool results.</summary>
    public string EverythingSeen => string.Join('\n', Calls.Select(c =>
        JsonSerializer.Serialize(new
        {
            instructions = c.Options?.Instructions,
            tools = c.Options?.Tools?.Select(t => t.Name),
            messages = c.Messages.Select(m => new { role = m.Role.Value, contents = m.Contents.Select(Describe) }),
        })));

    public IEnumerable<string> ToolNamesOfFirstCall => Calls[0].Options?.Tools?.Select(t => t.Name) ?? [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        Calls.Add(([.. messages], options));
        var step = script.Count > 0 ? script.Dequeue() : new Answer("(end of script)");
        switch (step)
        {
            case CallTool call:
                var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(call.Arguments, UiTools.Json));
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), call.Name, arguments)]);
                break;
            case Answer answer:
                foreach (var word in answer.Text.Split(' '))
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
                }

                break;
            case Fail:
                throw new HttpRequestException("The model provider is down.");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string Describe(AIContent content) => content switch
    {
        TextContent t => t.Text,
        FunctionCallContent c => $"call {c.Name} {JsonSerializer.Serialize(c.Arguments)}",
        FunctionResultContent r => $"result {JsonSerializer.Serialize(r.Result)}",
        _ => content.GetType().Name,
    };
}

/// <summary>
/// Stands in for the bank MCP server. The customer gets a read (accounts_list), a change (transfer_create) and the
/// host-only upload; the admin gets a read (trial_balance) and a change (loan_application_approve). Records which
/// session each run opened its tools for, and every change that really ran, with its confirmation id.
/// </summary>
public sealed class FakeBankTools : IBankTools
{
    public const string AccountsJson = """[{"number":"5555 0000 1","balance":{"amount":1200.00,"currency":"SEK"}}]""";

    public const string TrialBalanceJson = """{"currency":"SEK","totalDebit":100.00,"totalCredit":100.00}""";

    public const string ReceiptJson = """{"transferId":"t-1","amount":{"amount":"250.00","currency":"SEK"}}""";

    public List<(string Token, Guid? CustomerId)> Opened { get; } = [];

    /// <summary>Changes that reached the "bank": tool, confirmation id (idempotency key) and arguments.</summary>
    public List<(string Tool, string? ConfirmationId, string Arguments)> Ran { get; } = [];

    public bool Down { get; set; }

    public Task<BankToolSet> OpenAsync(HostSession session, CancellationToken ct, string? confirmationId = null)
    {
        if (Down)
        {
            throw new HttpRequestException("Connection refused");
        }

        Opened.Add((session.BankToken, session.Principal.CustomerId));
        if (session.Principal.Role == Role.Admin)
        {
            var trialBalance = AIFunctionFactory.Create((string currency) => TrialBalanceJson, "trial_balance", "The trial balance.");
            var approve = AIFunctionFactory.Create(
                (Guid applicationId) =>
                {
                    Ran.Add(("loan_application_approve", confirmationId, JsonSerializer.Serialize(new { applicationId })));
                    return """{"status":"Approved"}""";
                },
                "loan_application_approve", "Approves an application.");
            return Task.FromResult(new BankToolSet([trialBalance], [approve], []));
        }

        var accounts = AIFunctionFactory.Create(() => AccountsJson, "accounts_list", "The customer's accounts.");
        var transfer = AIFunctionFactory.Create(
            (Guid fromAccountId, Guid toAccountId, decimal amount, string currency) =>
            {
                Ran.Add(("transfer_create", confirmationId, JsonSerializer.Serialize(new { fromAccountId, toAccountId, amount, currency })));
                return ReceiptJson;
            },
            "transfer_create", "Moves money between the customer's accounts.");
        var upload = AIFunctionFactory.Create(
            (Guid applicationId, string fileName, string contentType, string contentBase64) =>
            {
                Ran.Add(("document_upload", confirmationId, JsonSerializer.Serialize(new { applicationId, fileName, contentType, size = Convert.FromBase64String(contentBase64).Length })));
                return """{"id":"i-1"}""";
            },
            "document_upload", "Host only.");
        return Task.FromResult(new BankToolSet([accounts], [transfer], [upload]));
    }
}
