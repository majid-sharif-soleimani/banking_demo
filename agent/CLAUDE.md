# agent

Phase 2: the chat UI and the agent host in front of the bank (`phase2-design-plan.md`, workspace ADR 0008). A client of the bank, not a bank service: no Kafka, no events, reaches the bank only through the gateway with `X-Api-Key`. Stage files: `docs/stages/phase-2/`.

## Owned data

Schema `agent` (role `agent`, created by `core_banking/bank-platform/db/init/002-agent-role-and-schema.sql` when `AGENT_DB_PASSWORD` is set):

- `chat`: one per login; `Open` until logout or the session ends, then `Closed` with `closed_reason` (`logout`, `session_expired`, `session_replaced`, `new_login`). Kept forever.
- `chat_message`: `sequence` unique per chat, `author` `user` or `assistant`, `kind` (`MessageKind`: `text`, components such as `table`, `confirmation`, and the person's actions `form_values`, `change_result`, `document`; JSON content for all but text).

Never stored or logged: passwords, bank tokens (tests check both).

## Behaviour

- `POST /auth/login` → identity through the gateway; opens a new chat; `HostSessions` (memory) keeps the bank token; the browser gets `bank_chat_session` (`HttpOnly`, `Secure`, `SameSite=Strict`). `POST /auth/logout`, `GET /auth/me`.
- `POST /agent`: AG-UI run (SSE) on `AGUI.Abstractions` types. Uses only the last user message; history from the database; writes only to the session's open chat. Every run checks the bank session (and keeps it alive). An ended session is a `RUN_ERROR` with its code; no session at all is HTTP 401.
- State-changing calls need `X-Bank-Chat: 1` (403 `missing_client_header`).
- The UI (`chat-ui/`) is built into `src/Bank.Agent.Host/wwwroot` and served from the same origin.
- The model sits behind `IChatClient`: Claude (Anthropic SDK) when `Agent:Model:ApiKey` is set, else `EchoChatClient` (Production refuses to start without a key). Each run builds a `ChatClientAgent` with `AgentInstructions.For(principal)`, the role's bank tools and the UI tools.
- `Bank.Agent.Mcp` (resource `bank-mcp`): MCP server, stateless, one path per role: `/mcp/customer` and `/mcp/admin` (agent ADR 0004). `ToolSets.KeepToolsOfPath` (`ConfigureSessionOptions`) keeps per request only that path's tools; `SharedTools` are on both. The host connects once per run (`McpBankTools.OpenAsync`, path from the session's role) with `Authorization: Bearer <token>`, plus `X-Customer-Id` for a customer; the model never sees either. Admin tools take a customer id; the bank's staff-only checks are the real boundary.
- UI tools (`UiTools`: table, card, chart, diagram, form, request_document) run in the host, are checked (`UiComponentCheck`, `FormCheck`), and reach the UI as finished AG-UI tool calls; bank tool calls never do. Tool calls are logged by name and outcome only (agent ADR 0002).
- Changes (agent ADR 0003): MCP tools with `ReadOnly = false` reach the model behind `ConfirmFirst`, which only stores a `PendingChange` in `HostSession.Pending` and shows `show_confirmation`. `POST /agent/changes/{id}/confirm|cancel`, `/agent/forms/{id}`, `/agent/documents/{id}` (`ActionEndpoints`) answer a pending item once; a confirmed change runs with `Idempotency-Key: <id>` (the MCP server refuses changes without it). The UI then appends "I confirmed the change." so the agent continues.
- `GET /chats`, `GET /chats/{id}` (`ChatEndpoints`): old chats, read-only; admin reads of others' chats are logged.

## Gotchas

- `wwwroot` is build output (gitignored). Run `npm run build` in `chat-ui` before running the host to see the UI. `dotnet publish` builds it first (target `BuildChatUi`, before `ResolveProjectStaticWebAssets`; running it later leaves a stale asset list). Clear `bin/Release` and `obj/Release` if old hashed files show up in the image.
- The login screen says the data is test data and shows the test logins (ADR 0007 makes them public on purpose).
- `SessionSweeper` (hosted service) closes chats left open by a previous start, and every `Agent:SweepInterval` closes the chats of idle sessions. Tests call `AgentApp.SweepAsync()` after moving `TestClock`.
- A bank outage during a run (`bank_unavailable`) keeps the chat open in the UI with a note; only session codes send the person to the login.
- A newer login of the same user marks the old host session ended; its browser hears `session_replaced` once (ADR 0001, point 6).
- A new MCP tool goes in the class of its role (`CustomerTools`, `AdminTools`, their `*ChangeTools`, or `SharedTools`); the per-path tool list tests catch a wrong place.
- Integration tests replace the gateway with `StubGateway` (an `HttpMessageHandler`), the clock with `TestClock`, the bank tools with `FakeBankTools` and, where needed, the model with `ScriptedModel`. `Bank.Agent.Mcp.Tests` runs the MCP server against `FakeGateway` (no Docker).
- Migrations: `dotnet tool restore && dotnet tool run dotnet-ef migrations add <Name> --project src/Bank.Agent.Host --output-dir Data/Migrations`.

## Commands

```bash
dotnet build && dotnet test                          # needs Docker (Testcontainers PostgreSQL)
(cd chat-ui && npm ci && npm test && npm run build)  # component tests, then the build into wwwroot
(cd tests/chat-ui-e2e && npm ci && npx playwright test)   # needs the AppHost running (see its README)
```
