using System.Text.Json;
using Bank.Agent.Host.Features.Agent;
using Microsoft.Extensions.AI;

namespace Bank.Agent.UnitTests;

public class ToolResultTests
{
    [Fact]
    public void A_successful_MCP_answer_is_its_text_without_any_wrapping()
    {
        var answer = ToolResult.Read(new TextContent("""{"customers":[]}"""));

        answer.Done.ShouldBeTrue();
        answer.Text.ShouldBe("""{"customers":[]}""");
    }

    [Fact]
    public void An_MCP_error_is_a_refusal_with_the_bank_s_text()
    {
        using var result = JsonDocument.Parse("""{"content":[{"type":"text","text":"insufficient_funds"}],"isError":true}""");

        var answer = ToolResult.Read(result.RootElement);

        answer.Done.ShouldBeFalse();
        answer.Text.ShouldBe("insufficient_funds");
    }

    [Fact]
    public void A_plain_string_stays_as_it_is() =>
        ToolResult.Read("receipt").ShouldBe(new ToolAnswer(true, "receipt"));
}
