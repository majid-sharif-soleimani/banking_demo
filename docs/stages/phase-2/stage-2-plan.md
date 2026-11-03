# Phase 2, stage 2 plan: First agent, customer reads

Spec: `docs/stages/phase-2/stage-2.md`. Status: built 2026-11-03; done-when commands green. Decisions: agent ADR 0002 (Claude, on Majid's delegation). Learning document: `docs/learning/phase-2/stage-2-first-agent.md`.

## Context

Stage 1 built the frame: a login the model never sees, the token kept in the host, a new chat per login. Stage 2 puts Claude inside that frame for the customer's read questions. The goal is the simplest design that keeps the token out of the model, lets the bank decide every number, and shows lists as tables and records as cards.

## Slices

### 1. The bank MCP server

- Project `agent/src/Bank.Agent.Mcp` (`CustomerTools`, `BankApi`, `Program.cs`); tests `agent/tests/Bank.Agent.Mcp.Tests` (`McpServerApp`, `FakeGateway`).
- Tests first: tool list; each tool's path, token and key; tool errors for 401 and 400; no call without token or customer id.

### 2. The agent with Claude

- Files: `Features/Agent/LanguageModel.cs`, `Features/Agent/BankTools.cs`, `Logic/AgentInstructions.cs`, the run in `Features/AgUi/AgUiEndpoint.cs`.
- Tests first: instructions per role (unit); bank tools get the session's token, the model never sees it; admin has no bank tools; MCP or model down.

### 3. Tables and cards as UI tools

- Files: `Features/Agent/UiTools.cs`, `Logic/UiComponentCheck.cs`, `Features/AgUi/TurnWriter.cs`; migration `AddMessageKind` (`chat_message.kind`).
- Tests first: table and card checks (unit); events and stored kinds; a broken table refused; history in the next turn; tool calls logged without data.

### 4. The chat UI draws them

- Files: `chat-ui/src/components.tsx` (+ test), `ChatView.tsx`, `styles.css`.
- Tests first: table cells and alignment, text never HTML, empty and partial tables, card fields.

## Wiring

AppHost resource `bank-mcp` and parameter `anthropic-api-key`; home compose service `bank-mcp` and `env/bank-mcp.env`; `home-hosting.md` section 8.

## Manual check (2026-11-03, AppHost)

- `GET /health` of `bank-mcp` and `agent`: 200.
- With an admin token from `/api/identity/login`: `tools/list` returned the five tools; `business_date` returned `{"date":"2026-01-01","status":"Open"}`; `loan_products_list` returned the seeded products. With a wrong token: `isError: true`, "The bank session has ended".
- Playwright (`agent/tests/chat-ui-e2e`, echo without a key): 5 passed.
- Not checked: a conversation with the real model (needs Majid's Anthropic key).

## Done-when results (2026-11-03)

- `agent`: unit 34, MCP 10, integration 35 passed.
- `agent/chat-ui`: 20 passed; build OK.
- `core_banking/bank-platform`: platform 55, end-to-end 5 passed.

## Blocking questions

None.
