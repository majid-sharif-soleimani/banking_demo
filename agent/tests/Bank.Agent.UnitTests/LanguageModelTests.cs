using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Bank.Agent.Host.Features.Agent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Bank.Agent.UnitTests;

/// <summary>
/// What really goes over the wire to the model provider, caught by a fake HTTP handler: no network, no key and no cost.
/// Caching and clearing only save money if these fields are in the request, and a typo here would show up only on the bill.
/// </summary>
public class LanguageModelTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        public string Beta { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            Beta = string.Join(",", request.Headers.TryGetValues("anthropic-beta", out var values) ? values : []);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"m","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"Hi"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<(JsonElement Body, string Beta)> SendAsync(ModelOptions options)
    {
        var capture = new Capture();
        var client = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(capture) };
        var model = LanguageModel.Claude(client, options);

        await model.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], new ChatOptions { MaxOutputTokens = 123 }, TestContext.Current.CancellationToken);

        return (JsonDocument.Parse(capture.Body).RootElement, capture.Beta);
    }

    [Fact]
    public async Task Every_call_asks_for_the_cache_and_keeps_the_model_and_the_output_limit()
    {
        var (body, _) = await SendAsync(new ModelOptions { Name = "claude-sonnet-5-5" });

        body.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
        body.GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
        body.GetProperty("max_tokens").GetInt32().ShouldBe(123);
    }

    [Fact]
    public async Task Every_call_asks_to_clear_old_tool_results_with_the_configured_limits()
    {
        var (body, beta) = await SendAsync(new ModelOptions { ClearToolResultsAbove = 111, KeepToolResults = 2, ClearToolResultsAtLeast = 33 });

        var edit = body.GetProperty("context_management").GetProperty("edits")[0];
        edit.GetProperty("type").GetString().ShouldBe("clear_tool_uses_20250919");
        edit.GetProperty("trigger").GetProperty("type").GetString().ShouldBe("input_tokens");
        edit.GetProperty("trigger").GetProperty("value").GetInt32().ShouldBe(111);
        edit.GetProperty("keep").GetProperty("value").GetInt32().ShouldBe(2);
        edit.GetProperty("clear_at_least").GetProperty("value").GetInt32().ShouldBe(33);
        beta.ShouldContain("context-management-2025-06-27");
    }

    [Fact]
    public async Task The_instructions_of_an_agent_carry_their_own_cache_point()
    {
        var capture = new Capture();
        var client = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(capture) };
        var agent = new ChatClientAgent(LanguageModel.Claude(client, new ModelOptions()), new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "Be kind." },
        });

        await agent.RunAsync("Hi", cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(capture.Body).RootElement;
        var system = body.GetProperty("system").EnumerateArray().ToList();
        system.Count(b => b.GetProperty("text").GetString() == "Be kind.").ShouldBe(1);
        system.ShouldHaveSingleItem().GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
        body.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
    }

    [Fact]
    public void The_tokens_written_to_the_cache_are_read_from_the_usage()
    {
        LanguageModel.CacheWritten(new UsageDetails()).ShouldBe(0);
        LanguageModel.CacheWritten(new UsageDetails { AdditionalCounts = new() { ["CacheCreationInputTokens"] = 20 } }).ShouldBe(20);
    }

    [Fact]
    public void A_provider_error_is_described_by_its_status_and_message()
    {
        var error = new Anthropic.Exceptions.AnthropicBadRequestException(new HttpRequestException("x"))
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = """{"type":"error","error":{"type":"invalid_request_error","message":"You have reached your usage limit."}}""",
        };

        LanguageModel.Describe(error).ShouldBe("AnthropicBadRequestException 400: You have reached your usage limit.");
    }

    [Fact]
    public void Any_other_error_is_described_by_its_type_only() =>
        LanguageModel.Describe(new InvalidOperationException("holds a secret")).ShouldBe("InvalidOperationException");
}
