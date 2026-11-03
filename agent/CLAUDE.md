# agent

Phase 2: the chat UI and the agent host in front of the bank (`phase2-design-plan.md`, workspace ADR 0008). A client of the bank, not a bank service: no Kafka, no events, reaches the bank only through the gateway with `X-Api-Key`. Stage files: `docs/stages/phase-2/`.

## Owned data

Schema `agent` (role `agent`, created by `core_banking/bank-platform/db/init/002-agent-role-and-schema.sql` when `AGENT_DB_PASSWORD` is set):

- `chat`: one per login; `Open` until logout or the session ends, then `Closed` with `closed_reason` (`logout`, `session_expired`, `session_replaced`, `new_login`). Kept forever.
- `chat_message`: `sequence` unique per chat, `author` `user` or `assistant`.

Never stored or logged: passwords, bank tokens (tests check both).

## Behaviour

- `POST /auth/login` → identity through the gateway; opens a new chat; `HostSessions` (memory) keeps the bank token; the browser gets `bank_chat_session` (`HttpOnly`, `Secure`, `SameSite=Strict`). `POST /auth/logout`, `GET /auth/me`.
- `POST /agent`: AG-UI run (SSE) on `AGUI.Abstractions` types. Uses only the last user message; history from the database; writes only to the session's open chat. Every run checks the bank session (and keeps it alive). An ended session is a `RUN_ERROR` with its code; no session at all is HTTP 401.
- State-changing calls need `X-Bank-Chat: 1` (403 `missing_client_header`).
- The UI (`chat-ui/`) is built into `src/Bank.Agent.Host/wwwroot` and served from the same origin.
- The model sits behind `IChatClient`; stage 1 uses `EchoChatClient`.

## Gotchas

- `wwwroot` is build output (gitignored). Run `npm run build` in `chat-ui` before running the host to see the UI; publishing the host builds it.
- A newer login of the same user marks the old host session ended; its browser hears `session_replaced` once (ADR 0001, point 6).
- Integration tests replace the gateway with `StubGateway` (an `HttpMessageHandler`) and the clock with `TestClock`.

## Commands

```bash
dotnet build && dotnet test                          # needs Docker (Testcontainers PostgreSQL)
(cd chat-ui && npm ci && npm test && npm run build)  # component tests, then the build into wwwroot
(cd tests/chat-ui-e2e && npm ci && npx playwright test)   # needs the AppHost running (see its README)
```
