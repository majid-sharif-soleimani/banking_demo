# Phase 2, stage 1: Chat shell and login

Goal: a person opens the chat in the browser, logs in with a form inside the chat, and lands in a new, empty chat that has no trace of the login. They can send messages, which the agent host stores and echoes back, log out, and are told when the bank session ends. There is no language model yet: this stage builds and tests the security frame around it.

Read first: `phase2-design-plan.md` sections 2.1, 3, 4.1, 4.4, 6 and 7.1; workspace ADR 0007 (test logins and sessions); `core_banking/bank-gateway/CLAUDE.md`; `docs/implementation-decisions.md` (Database, Testing).

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Backend for frontend (BFF), cookie security (`HttpOnly`, `Secure`, `SameSite`), CSRF, server-side sessions, the AG-UI protocol (runs, events, streaming), keeping secrets out of logs and storage | The React chat library and whether a Node runtime sits in the middle; where the agent host keeps its sessions (memory or PostgreSQL); how the chat UI is served (by the agent host or by its own container) | `ChatAccess` in `Logic/` (who may read and write which chat), with its unit tests |

Slices: (1) agent host and the `agent` schema, (2) login, logout and the server session, (3) chat storage and the AG-UI echo endpoint, (4) chat UI, (5) end-to-end check in a browser.

## Folders created

`agent/` at the repository root, next to `core_banking/`:

```
agent/
  CLAUDE.md
  docs/adr/
  src/Bank.Agent.Host/        ← ASP.NET Core: BFF + AG-UI endpoint
    Data/                     ← AgentDbContext, entities, migrations (schema "agent")
    Features/                 ← Auth, Chats, AgUi
    Logic/                    ← pure rules (ChatAccess, SessionEnd)
  chat-ui/                    ← React + TypeScript (Vite)
  tests/Bank.Agent.UnitTests/
  tests/Bank.Agent.IntegrationTests/
  tests/chat-ui-e2e/          ← browser test (Playwright)
```

The bank MCP server (`agent/src/Bank.Agent.Mcp/`) is created in stage 2.

## Agent host

- Registered in the Aspire AppHost as `agent`. It references and waits for the gateway and PostgreSQL. It uses `Bank.ServiceDefaults` (OpenTelemetry, health). It has no Kafka and publishes no events: it is a client of the bank, not a bank service.
- Calls the bank only through the gateway, with `X-Api-Key` from configuration (`Agent:Gateway:ApiKey`, a secret: user secrets locally, `env/agent.env` at home).
- Time for chats and sessions is wall-clock time from `TimeProvider`, like identity. The business date does not apply.

## Data (schema `agent`, own database role, like every bank service)

| Table | Columns |
| --- | --- |
| `chat` | `id`, `username`, `role` (`customer` or `admin`), `customer_id` (null for admin), `status` (`Open`, `Closed`), `opened_at`, `closed_at`, `closed_reason` (`logout`, `session_expired`, `session_replaced`, `new_login`), `CreatedAt`, `PerformedBy`, `Version` |
| `chat_message` | `id`, `chat_id`, `sequence`, `author` (`user`, `assistant`), `content` (text in this stage), `CreatedAt`, `PerformedBy`, `Version` |

No column, row or log line holds a password or a bank token.

## Endpoints (agent host)

| Endpoint | Behaviour |
| --- | --- |
| `POST /auth/login` | Body `{username, password}`. Calls `POST /api/identity/login` through the gateway. On success: ends the caller's previous host session if any (its chat closes with `new_login`), starts a host session holding the bank token, opens a new chat, sets the session cookie, returns `{role, name, chatId}`. On failure: 401 `invalid_credentials` (same body for every cause), 503 `bank_unavailable`. |
| `POST /auth/logout` | Ends the host session and the bank session (`POST /api/identity/logout`), closes the chat with `logout`, clears the cookie. 204. |
| `GET /auth/me` | `{role, name, chatId}` of the current session, or 401 with `missing_session`, `session_expired` or `session_replaced`. |
| `POST /agent` | The AG-UI endpoint. Needs a valid host session and the session's open chat. Each run first checks the bank session (`GET /api/identity/sessions/current`), which also keeps it alive while the person chats. Stores the user message, answers with an echo (`You said: ...`) streamed as AG-UI events, stores the answer. When the bank session has ended, the run fails with an AG-UI error event carrying `session_expired` or `session_replaced`, the chat closes with that reason, and the host session ends. |

Cookie: random opaque session id, `HttpOnly`, `Secure` (outside Development), `SameSite=Strict`, path `/`. State-changing endpoints also require a custom request header, so a form on another site cannot call them.

## Chat UI

- React and TypeScript, built with Vite. Same origin as the agent host, so the cookie works without CORS.
- First screen: a login form drawn by the UI itself (username, password, submit). Its values go only to `POST /auth/login`. They never become chat messages and are cleared from memory after the call.
- After login: a new chat view, empty, with the person's name and role and a logout button.
- Messages go to `POST /agent` and the echo streams back.
- When a run fails with `session_expired` or `session_replaced`, or `GET /auth/me` returns them, the chat is locked, a banner says why ("You were away for 5 minutes", "You logged in somewhere else"), and the login form appears. A new login opens a new chat.
- English only in this stage. Persian (right to left) and Swedish come in stage 6.

## Tests (minimum)

- Unit (`Bank.Agent.UnitTests`): `ChatAccess` (a customer reads and writes only their own open chat; the admin reads every chat; nobody writes a closed chat), `SessionEnd` (identity error codes to chat close reasons and UI messages).
- Integration (`Bank.Agent.IntegrationTests`, Testcontainers PostgreSQL, a stub gateway as in the gateway tests): login sets a cookie with the right flags; wrong login 401 with one body; bank down 503; a second login closes the first chat with `new_login`; logout closes the chat and calls identity logout; an echo run stores both messages; a run after `session_expired` or `session_replaced` closes the chat and ends the host session; requests without the custom header are refused; after a full login and chat, neither the database nor the captured logs contain the password or the token.
- Browser (`chat-ui-e2e`, Playwright, against the AppHost): log in as a customer; the new chat is empty and the page holds no trace of the login values; send a message and see the echo; log out and see the login form.
- Platform: the AppHost starts `agent` healthy.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd agent/chat-ui && npm ci && npm run build && npm test)
(cd agent/tests/chat-ui-e2e && npm ci && npx playwright test)
(cd core_banking/bank-platform && dotnet test)
```
