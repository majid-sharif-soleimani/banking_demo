# Phase 2, stage 2: First agent, customer reads

Goal: a logged-in customer asks the chat about their money ("What are my balances?", "Show my statement for October", "When is my next loan installment?", "Which loans do you offer?") and a real language model (Claude) answers with the bank's own numbers, as text, tables and cards. The model reaches the bank only through tools in a new bank MCP server, and never sees the bank token. Nothing changes data yet: changes, forms and confirmations are stage 3.

Read first: `phase2-design-plan.md` sections 2.2 (the simple customer rows), 3, 4.2, 5 and 6; agent ADR 0001; `agent/CLAUDE.md`.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Tool calling (function calling) and the agent loop, the Model Context Protocol (MCP: server, client, tools), `IChatClient` and Microsoft Agent Framework's `ChatClientAgent`, system instructions, UI components as tools (generative UI), keeping the token out of the model's context | None asked: Majid delegated the decisions of this stage to Claude (2026-11-03: "do not ask me ADR questions, keep the design simple"). Claude's choices are in agent ADR 0002. | Nothing (phase 2: Claude writes all) |

Slices: (1) the bank MCP server with the customer read tools, (2) the agent with Claude behind `IChatClient`, (3) tables and cards as UI tools, stored in the chat, (4) the chat UI renders them.

## Folders created

```
agent/
  src/Bank.Agent.Mcp/              ← ASP.NET Core MCP server (HTTP), customer read tools, no state
  tests/Bank.Agent.Mcp.Tests/      ← the MCP server against a stub gateway (no Docker)
```

## Bank MCP server

- Registered in the AppHost as `bank-mcp`; references and waits for the gateway. Not published outside the bank network.
- One endpoint, `/mcp/customer`, with the customer read tools only. The admin tool set comes in stage 4.
- The agent host sends `Authorization: Bearer <bank token>` and `X-Customer-Id` as headers of the MCP connection. The server forwards the token to the gateway with `X-Api-Key` and uses the customer id only to build the URL. The gateway still decides what the caller may see.

| Tool | Bank call |
| --- | --- |
| `business_date` | `GET /api/operations/business-days/current` |
| `accounts_list` | `GET /api/accounts/accounts?customerId=` |
| `account_statement` | `GET /api/accounts/accounts/{id}/statement?from=&to=` |
| `loans_list` | `GET /api/lending/loans?customerId=` |
| `loan_products_list` | `GET /api/lending/loan-products` |

## Agent host

- The model: Anthropic's official C# SDK as `IChatClient` (`Agent:Model:Name`, default `claude-opus-5-5`; key `Agent:Model:ApiKey`, a secret). Without a key the echo stays, in Development and tests only; outside Development the host refuses to start without one.
- Each run builds a `ChatClientAgent` with the role's instructions, the bank tools (customers only) and the UI tools `show_table` and `show_card`.
- UI tool calls stream to the UI as AG-UI `TOOL_CALL_*` events; bank tool calls do not. Every tool call is logged with chat id, role, tool name and outcome, without data.
- `chat_message` gets a `kind` column: `text`, `table` or `card`. A table or card stores its JSON; the model sees it again in later turns.

## Chat UI

- Renders `show_table` as a table and `show_card` as a labelled card inside the assistant's message.
- Assistant text is plain text (the instructions ask for no Markdown).

## Tests (minimum)

- Unit: the instructions per role; the table check (every row has one cell per column).
- MCP tests: the tool list; each tool calls the right bank path with the token, the API key and the session's customer id; a refused session and a bank error come back as tool errors.
- Integration (host): a scripted model calls a bank tool and `show_table`; the UI gets the table events, not the bank call; the messages are stored with their kind; the bank tools get the session's token, the model never does; the admin gets no bank tools; a bad table is refused and the model is told why.
- Chat UI: the table and the card render.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd agent/chat-ui && npm ci && npm test && npm run build)
(cd core_banking/bank-platform && dotnet test)
```

A manual check with a real key (Majid's, in user secrets) is in the learning document.
