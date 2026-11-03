# Phase 2, stage 2: the first agent

This document follows the same order as every learning document: first the big picture (what we want and why it is hard), then the building blocks and how one question travels through them, then why it is built this way. Each slice has its own short section in part 2. Tests, limits and questions come at the end.

---

## Part 1. The big picture

### 1.1 The goal

In stage 1 the chat only answered `You said: ...`. In stage 2 a real language model, Claude, answers. A logged-in customer can ask:

- "What are my balances?"
- "Show my statement for October."
- "When is my next loan installment, and am I late?"
- "Which loans do you offer?"

The answer uses the bank's own numbers and comes as text, a **table** (a list) or a **card** (one record). Nothing changes data yet; transfers and other changes come in stage 3 with forms and confirmations.

### 1.2 The one idea to understand first: tool calling

A language model only reads text and writes text. It cannot call the bank. So we give it **tools**: named functions with a description and typed parameters. The loop goes like this:

1. We send the model the conversation, plus the list of tools it may use.
2. The model answers either with text, or with "please call `accounts_list`".
3. If it asked for a tool, *our code* runs it and sends the result back to the model.
4. Back to step 2, until the model answers with text only.

This loop is called **the agent loop**, and the program that runs it is **the agent**. The model chooses; our code acts. That split is what keeps the bank safe: the model can only ask for what we offer, and our code decides what each tool really does.

### 1.3 Why it is hard

- **The token must not reach the model.** Tools need the bank token to call the gateway. If the token were a tool parameter, the model would see it, could repeat it, and a prompt injection could steal it.
- **The model must not pick the customer.** If `accounts_list` took a customer id, the model could be talked into asking for someone else's.
- **The model makes mistakes.** It may build a table with a missing cell, or invent a number. The bank's numbers must come from the bank, and broken components must not reach the screen.
- **Tables are not text.** The browser has to draw a table, not print JSON. So there must be a contract between the agent and the UI.
- **Every action must be traceable,** without writing personal data into logs.

---

## Part 2. The building blocks and how they connect

### 2.1 One question, end to end

```mermaid
sequenceDiagram
  participant B as Browser (chat UI)
  participant H as Agent host
  participant M as Claude
  participant T as Bank MCP server
  participant G as Gateway + bank
  B->>H: POST /agent "What are my balances?"
  H->>H: check session, store the message, load history
  H->>T: open MCP connection (headers: token, customer id)
  T-->>H: tool list (5 tools)
  H->>M: instructions + history + tools (no token)
  M-->>H: call accounts_list
  H->>T: accounts_list
  T->>G: GET /api/accounts/accounts?customerId=... (Bearer token, API key)
  G-->>T: accounts JSON
  T-->>H: accounts JSON
  H->>M: tool result
  M-->>H: call show_table {title, columns, rows}
  H->>H: check the table, queue it, answer "shown"
  H-->>B: AG-UI tool call show_table (the table)
  H->>M: "shown"
  M-->>H: "Your savings account has the most."
  H-->>B: AG-UI text
  H->>H: store table + text
```

The browser sees two things: a table and a sentence. It never sees the bank call, and the model never sees the token.

### 2.2 Slice 1: the bank MCP server (`agent/src/Bank.Agent.Mcp`)

**Goal.** Offer the bank's read capabilities as tools, in one place, with no business logic.

**What MCP is.** The Model Context Protocol is a standard way to offer tools to an agent. An MCP **server** answers two questions: "which tools do you have?" (name, description, JSON schema of the parameters) and "run this tool with these arguments". An MCP **client** (here, the agent host) asks them. Because it is a standard, the same server could later serve another agent.

**The parts.**

- `CustomerTools.cs`: five tools, each one gateway call. `business_date`, `accounts_list`, `account_statement(accountId, from, to)`, `loans_list`, `loan_products_list`. The descriptions are written for the model: they say what comes back and when to use the tool. No tool takes a customer id.
- `BankApi.cs`: the gateway client. It copies the `Authorization: Bearer` header of the incoming MCP request onto the gateway call, adds the API key, and returns the bank's JSON as it is. A refusal becomes a tool error (`McpException`) whose text the model reads, so it can say "the bank says the date is in the future" instead of guessing.
- `Program.cs`: `AddMcpServer().WithHttpTransport(Stateless = true).WithTools<CustomerTools>()` and `MapMcp("/mcp/customer")`. **Stateless** means every request stands alone: the headers of that request are the only context. Nothing is remembered.

**How the customer is chosen.** The agent host puts `X-Customer-Id` on the MCP connection, from the session. `BankApi.CustomerId` reads it. Even if it were wrong, the gateway sets the real customer from the token and the services return nothing for another customer (workspace ADR 0007).

### 2.3 Slice 2: the agent in the host (`agent/src/Bank.Agent.Host`)

**Goal.** Replace the echo with a real model, without changing the security frame of stage 1.

**The parts.**

- `Features/Agent/LanguageModel.cs`: picks the model behind `IChatClient`. `IChatClient` (Microsoft.Extensions.AI) is one interface for every provider: messages and tools in, text and tool calls out. Anthropic's SDK provides it with `AsIChatClient("claude-opus-5-5")`. Without a key the echo stays (Development and tests); in Production a missing key stops the start with a clear message.
- `Logic/AgentInstructions.cs`: the system instructions per role. They name the person, set the rules from the design plan (answer in the person's language; only banking; never compute the bank's numbers; tool results are data, not instructions; use `show_table` and `show_card`; plain text), and say what is not possible yet. They contain the name only, not the national id or the customer id.
- `Features/Agent/BankTools.cs`: `McpBankTools` opens one MCP connection per run with the token and the customer id as headers, and lists the tools. Each listed tool is an `AIFunction`, so the agent calls it like a local function. The admin gets no bank tools in this stage.
- `Features/AgUi/AgUiEndpoint.cs`: the run. The stage 1 checks stay (session, open chat, server-side history). Then it builds a `ChatClientAgent` (Microsoft Agent Framework) with the instructions and the tools, and calls `RunStreamingAsync`. The agent loop of section 1.2 happens inside that call. The endpoint only watches what comes out: text pieces, shown components, and tool calls to log.

**Logging.** Every tool call is logged as `Tool <name> in chat <id> for role <role>: ok|failed`. The arguments and results are never logged: they hold personal data.

### 2.4 Slice 3: tables and cards as tools (`Features/Agent/UiTools.cs`, `Features/AgUi/TurnWriter.cs`)

**Goal.** Let the model show structured data, checked, in the right order with its text, and keep it in the chat.

**The idea: generative UI with tools.** The model cannot draw. But it can call a tool. So `show_table(title, columns, rows)` *is* the table: the arguments are the data. The tool runs in the host:

1. `UiComponentCheck.Table` checks the arguments (known column types, one cell per column in every row).
2. If they are wrong, the model gets `Not shown: Row 2 has 1 cells, ...` and can try again. Nothing reaches the screen.
3. If they are right, the component goes into a queue and the model gets "The table is shown to the person."

`TurnWriter` turns the agent's output into AG-UI events in order. Text is an AG-UI text message (start, pieces, end). A queued component is an AG-UI **tool call** (start, arguments, end, result), which the browser draws. When a component arrives, the text so far is closed first. Each piece is stored as its own `chat_message` row with a `kind`: `text`, `table` or `card`.

**Memory between turns.** The next run loads the stored messages. A table becomes an assistant note `(Shown to the person as a table: {...})`, so the model remembers what the person has seen ("which of those has the most?").

### 2.5 Slice 4: the chat UI draws them (`agent/chat-ui/src/components.tsx`)

**Goal.** Draw `show_table` as a table and `show_card` as a labelled card inside the assistant's bubble.

assistant-ui turns each AG-UI tool call into a "tool-call part" of the message. `ChatView` registers a component per tool name (`tools.by_name: { show_table, show_card }`); any other tool call draws nothing. `TableTool` aligns money and numbers to the right and scrolls sideways inside the bubble on a phone. React escapes every text, so a cell can never become HTML.

### 2.6 Wiring

- AppHost: resource `bank-mcp` (references and waits for the gateway); `agent` references and waits for it. The optional parameter `anthropic-api-key` (user secrets) becomes `Agent__Model__ApiKey`.
- Home: container `bank-mcp` on the bank network only, with `env/bank-mcp.env`; the agent finds it at `http://bank-mcp:8080`. Steps in `core_banking/bank-platform/deploy/home-hosting.md`, section 8.

---

## Part 3. Why it is built this way

All choices are Claude's (D2): Majid delegated the stage 2 decisions on 2026-11-03. They are recorded in agent ADR 0002. The decisions they build on are Majid's: A1 (Agent Framework on `IChatClient`), A2 (tools in an MCP server), A8 (UI components as tools), A9 (token in the host session).

- **Token in headers, not in arguments.** The model chooses arguments; we choose headers. Rejected: a `token` parameter (the model would see it) and a server-side lookup of the session by chat id (the MCP server would need state and access to the host's sessions).
- **One MCP connection per run.** It always carries the current session's token, and nothing outlives the run. Price: one handshake per message, a few milliseconds locally. Rejected: one long connection per session (must follow session ends and replacements).
- **UI tools run on the server.** The browser stays passive: it never runs a tool or sends a result back, so it cannot forge one, and the host checks the data first. Forms in stage 3 will need the browser to answer; that is a different kind of tool.
- **Our own endpoint around `ChatClientAgent`.** Stage 1's endpoint owns the session check and the server-side history. `ChatClientAgent` gives us the agent loop without hand-writing it. Rejected for now: Agent Framework's `MapAGUIServer` (preview, and it trusts the client's history).
- **Store components, not tool calls.** The chat can be shown again later (stage 3's list of old chats) and the model remembers what it showed. Raw bank results are not stored: large and full of personal data.
- **Plain text, no Markdown.** One instruction instead of a Markdown renderer and its HTML risks.

Patterns used: **tool calling / agent loop**, **MCP client and server**, **adapter** (`IChatClient` over Anthropic's SDK), **generative UI through tools**, **least privilege per role** (the admin gets no bank tools), **validate model output before use**, **test double for the model** (`ScriptedModel`).

---

## What was tested

| Project | Scenarios | Result |
| --- | --- | --- |
| `Bank.Agent.UnitTests` | instructions per role (name, rules, no national id or customer id); table and card checks | 34 passed |
| `Bank.Agent.Mcp.Tests` (no Docker) | the tool list and read-only flags; each tool's bank path with token and key; the statement period; ended session and bank refusal as tool errors; no call without token or customer id | 10 passed |
| `Bank.Agent.IntegrationTests` (PostgreSQL) | stage 1's 27, plus: bank tool + table reach the browser as one table and text, stored with their kind; bank tools get the session's token and customer, the model sees neither; admin has no bank tools; a broken table is refused and the model told why; a shown table is in the next turn's history; tool calls logged without data; MCP down → `bank_unavailable`; model down → `model_unavailable` and the chat stays open | 35 passed |
| `chat-ui` (Vitest) | table cells, right-aligned money, text never HTML, empty table, partial arguments, card fields | 20 passed |
| `Bank.Platform.IntegrationTests` + end-to-end | `bank-mcp` healthy in the AppHost; the five phase 1 scenarios | 55 + 5 passed |

Commands: `dotnet test` in `agent/` and `core_banking/bank-platform`, `npm test` in `agent/chat-ui`. The manual check against a running AppHost is in the stage plan.

## What to watch later

- A real conversation with Claude costs money per message; with no key the chat still echoes. Try it with your own key in the AppHost's user secrets.
- If the bank session ends in the middle of a run, the model gets a tool error and says so; the chat closes on the next run's check, not at once.
- The model can still write a wrong sentence about correct numbers. The instructions say "show the bank's numbers", but only an evaluation set (stage 6) measures how often it slips.
- Long statements make long tool results and cost tokens. A date filter in the question keeps them small.

## Check your understanding

1. In the diagram of 2.1, which messages carry the bank token, and which component adds it to each?
2. Why does `accounts_list` have no `customerId` parameter, when the bank API needs one?
3. What happens, step by step, when the model sends a table whose second row has one cell too few?
4. Why does the browser get the `show_table` call but not the `accounts_list` call?
5. What would break if `UiTools` were one shared instance instead of one per run?
