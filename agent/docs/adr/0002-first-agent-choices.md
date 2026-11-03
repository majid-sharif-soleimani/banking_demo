# 0002. Run the first agent on ChatClientAgent with MCP bank tools and server-side UI tools

- Status: Accepted
- Date: 2026-11-03
- Scope: agent (Bank.Agent.Host, Bank.Agent.Mcp, chat-ui), bank-platform
- Design plan reference: `phase2-design-plan.md` decisions A1, A2, A8, sections 3.3, 4.2, 5, 6; agent ADR 0001 point 4
- Decided by: Claude. Majid delegated the stage 2 decisions on 2026-11-03 ("do not ask me ADR questions; keep the design simple and readable").

## Context

Stage 2 puts a real language model behind the chat for the customer's read questions. Four things had to be chosen: how the agent loop runs, how the bank tools reach the agent without the token reaching the model, how tables and cards travel to the browser, and what the model remembers between turns. The goal was the simplest design a newcomer can follow, inside the decisions already in the design plan.

## Decision

1. **Our own AG-UI endpoint stays; the loop is Microsoft Agent Framework's `ChatClientAgent`.** Each run builds a `ChatClientAgent` over the configured `IChatClient` with the role's instructions and tools, and the endpoint turns its streamed updates into AG-UI events. Agent Framework's hosting package (`MapAGUIServer`) is not used (ADR 0001 point 4 left this open).
2. **Claude through Anthropic's official C# SDK** (`AnthropicClient.AsIChatClient`), model from `Agent:Model:Name` (default `claude-opus-5-5`), key `Agent:Model:ApiKey`. Without a key the stage 1 echo answers; in Production a missing key stops the start.
3. **A separate bank MCP server** (`Bank.Agent.Mcp`, Aspire resource `bank-mcp`) with the customer read tools at `/mcp/customer`, streamable HTTP, stateless. The host opens one MCP connection per run with `Authorization: Bearer <bank token>` and `X-Customer-Id` as headers; tools never take a customer id. The admin gets no bank tools in this stage.
4. **Tools return the bank's JSON unchanged.** A refused call is a tool error (`McpException`) with the bank's answer, so the model can explain it.
5. **UI tools run on the server.** `show_table` and `show_card` are host-side functions: they check the arguments (`Logic/UiComponentCheck`), queue the component, and answer the model "shown". The endpoint streams each queued component as a finished AG-UI tool call (start, args, end, result). Bank tool calls never reach the browser.
6. **Components are stored as messages.** `chat_message.kind` is `text`, `table` or `card`; a component's JSON is its content. In later turns the model sees it as an assistant note `(Shown to the person as a table: ...)`. Tool calls and raw tool results are not stored.
7. **Plain text answers.** The instructions ask for no Markdown, and the UI shows text with line breaks kept. No Markdown renderer is added.

## Rationale

The endpoint from stage 1 already owns the security checks (session, chat ownership, server-side history); keeping it means only the "answer" part changed. `ChatClientAgent` runs the tool loop (call, run tool, call again) so we do not hand-write it. A separate MCP server is what decision A2 asks for, and the per-run connection with headers is the plainest way to keep the token out of the model's view. Server-side UI tools keep the browser passive: it never executes a tool or sends results, so it cannot forge them, and the arguments are checked before anyone sees them.

## Alternatives considered

- `MapAGUIServer` from Agent Framework: rejected for now; a preview package, and it takes the history and thread id from the client, which ADR 0001 refused.
- MCP tools inside the host process: simpler, but against decision A2 and a weaker line between the agent and the bank.
- One long-lived MCP connection per session: fewer handshakes, but the connection would have to follow session ends and token changes. One per run cannot go stale.
- AG-UI frontend tools (declared by the browser, results sent back by it): needed for forms in stage 3, not for display-only components.
- Storing every tool call and result: a fuller replay, but large, full of personal data, and not needed for the model to follow the conversation.
- A Markdown renderer: nicer text, one more dependency and an HTML injection surface to review.

## Consequences

- The chat costs real money per message when a key is set. The model name is configuration.
- Each run pays one MCP handshake (a few milliseconds on the local network).
- A bank session that ends during a run shows up as a tool error the model explains; the next run's session check closes the chat.
- Stage 3 adds forms and confirmations as tools the browser answers, next to these server-side display tools.
