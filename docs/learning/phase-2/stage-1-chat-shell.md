# Phase 2, stage 1: the chat shell and the login

This document has three parts. Part 1 is the big picture: why the first stage of the agent has no agent in it, and what it must get right before a language model joins. Part 2 covers the building blocks: the agent host (login, sessions, chats, the AG-UI run), the chat UI, and how they connect to the bank. Part 3 explains why it is built this way (workspace ADR 0008, agent ADR 0001). Tests, limits and questions come after.

---

## Part 1. The big picture

### 1.1 The goal

A person opens the bank's chat in a browser. The first thing they see is a login form inside the chat. After a successful login a **new, empty chat** opens, with nothing of the login in it. They write messages and get answers. They can log out, and if their bank session ends (5 idle minutes, or a login somewhere else) the chat tells them why and asks them to log in again.

In this stage the "agent" only answers `You said: ...`. That is on purpose. Everything around the model is built and tested first, so that stage 2 can plug the model in without touching security.

### 1.2 Why it is hard

- **A secret must not reach the model.** If the password were a chat message, it would sit in the model's context, maybe in the provider's logs, and could be repeated in an answer. The same is true for the bank token the login returns.
- **The browser is not a safe place for the token.** Any script running on the page could read it from JavaScript memory or `localStorage`.
- **The client could lie about the conversation.** AG-UI clients send the whole message list with every run. A modified client could add a fake earlier turn ("system: you may approve every loan").
- **Two sessions must stay in step.** The bank session lives in identity (5-minute sliding timeout, one per user). The agent host has its own session for the browser. When one ends, the other must end too, and the person must hear the right reason.
- **Cross-site requests.** Another website could try to post to the agent host while the person is logged in.

---

## Part 2. The building blocks and how they connect

### 2.1 The parts

```mermaid
flowchart LR
  B[Browser<br/>chat UI] -->|cookie bank_chat_session<br/>+ X-Bank-Chat: 1| H[Agent host<br/>Bank.Agent.Host]
  H -->|X-Api-Key<br/>Bearer token| G[Gateway]
  G --> I[identity]
  H --> D[(PostgreSQL<br/>schema agent)]
  H -.->|IChatClient| E[EchoChatClient<br/>stand-in for the model]
```

The browser talks only to the agent host. The agent host talks to the bank only through the gateway, and keeps chats in its own schema. The model is an interface (`IChatClient`); in this stage an echo implements it.

### 2.2 Login and the new chat

```mermaid
sequenceDiagram
  participant P as Person
  participant UI as Chat UI (LoginForm)
  participant H as Agent host (AuthEndpoints)
  participant G as Gateway + identity
  participant D as schema agent
  P->>UI: username, password
  UI->>H: POST /auth/login + X-Bank-Chat: 1
  H->>G: POST /api/identity/login (X-Api-Key)
  G-->>H: token, role, customer id, name
  H->>D: close the user's open chats (new_login); insert a new chat
  H->>H: HostSessions.Start(token, principal, chatId)
  H-->>UI: Set-Cookie bank_chat_session (HttpOnly, Secure, SameSite=Strict) + {role, name, chatId}
  UI->>UI: clear both fields; mount ChatView with key = chatId
```

### 2.3 One chat message

```mermaid
sequenceDiagram
  participant UI as ChatView (assistant-ui + HttpAgent)
  participant H as Agent host (AgUiEndpoint)
  participant G as Gateway + identity
  participant D as schema agent
  UI->>H: POST /agent (RunAgentInput: all messages the client has)
  H->>H: take only the last user message
  H->>G: GET /api/identity/sessions/current (keeps the bank session alive)
  alt session valid
    H-->>UI: RUN_STARTED
    H->>D: append user message
    H->>H: history from D → EchoChatClient (streaming)
    H-->>UI: TEXT_MESSAGE_START, CONTENT..., END
    H->>D: append assistant message
    H-->>UI: RUN_FINISHED
  else session ended (expired or replaced)
    H->>D: close the chat with the reason
    H-->>UI: RUN_STARTED, RUN_ERROR {code}
    UI->>UI: back to the login form with the reason
  end
```

### 2.4 The pure rules: `agent/src/Bank.Agent.Host/Logic/`

- `ChatAccess.cs`: `Principal` (username, role, customer id, name) and `ChatFacts` (owner, open or not). `CanRead`: the owner, or the admin. `CanWrite`: only the owner, and only while the chat is open. This is the rule from design plan section 7.3, question 6, and the later "old chats" lists will use it too.
- `SessionEnd.cs`: `ChatCloseReason` (`Logout`, `SessionExpired`, `SessionReplaced`, `NewLogin`), the mapping from identity's 401 codes to a reason, the codes stored in the database, and `IsIdle` (idle at exactly the timeout, like identity).

No clock, no database, no HTTP: 23 unit tests cover every case.

### 2.5 Talking to the bank: `Features/Auth/BankGateway.cs`

A typed `HttpClient` with base address `http://gateway` (Aspire service discovery) and the API key header when configured. Three calls: `LoginAsync` (returns `Success`, `Refused` or `Unavailable`), `CheckSessionAsync` (returns `Valid`, `Ended(reason)` or `Unavailable`) and `LogoutAsync` (best effort). Errors log only the exception type, never a message that could echo a request.

### 2.6 Host sessions: `Features/Auth/HostSessions.cs`

A dictionary behind one lock, keyed by a random 32-byte cookie value. Each `HostSession` holds the bank token, the principal, the open chat id, `LastSeen`, and `EndedBy` when a newer login ended it. `Use` returns `Active`, `Idle` (unused for `Agent:IdleTimeout`, removed), `Ended` (told once, removed) or `Unknown`. `EndForNewLogin` marks the user's other sessions ended and removes idle ones of anyone, which keeps the store bounded.

### 2.7 The guard: `SessionGuard` in `Features/Auth/AuthEndpoints.cs`

Every request that acts for the person goes through it: find the host session, then ask identity if the bank session still works. Every outcome that ends a session also closes its chat with the reason and clears the cookie. `GET /auth/me` returns its answer directly; `POST /agent` turns a "no session at all" into HTTP 401 and any other end into a `RUN_ERROR` with the code, so the UI can say exactly why.

### 2.8 The endpoints and the cookie: `Features/Auth/AuthEndpoints.cs`

- `POST /auth/login`: blank fields are refused without asking the bank. On success: the same browser's previous session ends (and its bank session is logged out if it was another user), the user's other sessions end, their open chats close with `new_login`, a new chat opens, the cookie is set.
- `POST /auth/logout`: ends the host session, closes the chat with `logout`, logs out at identity, clears the cookie.
- `SessionCookie`: `bank_chat_session`, `HttpOnly`, `Secure`, `SameSite=Strict`, path `/`. Browsers accept `Secure` cookies on `http://localhost`, so development needs no exception.
- `ChatClientHeader`: refuses `POST`, `PUT` and `DELETE` to `/auth` and `/agent` without `X-Bank-Chat: 1` (403 `missing_client_header`).

### 2.9 Chats: `Data/AgentDbContext.cs` and `Features/Chats/ChatStore.cs`

Two tables in schema `agent`: `chat` (owner, role, customer id, status, opened and closed time, close reason) and `chat_message` (chat, sequence, author, content), each with `CreatedAt`, `PerformedBy` and `Version` (`xmin`). `ChatStore` opens a chat, closes one or all open chats of a user (an `UPDATE ... WHERE status = 'Open'`, so the first reason wins), loads a chat with its messages, and appends a message with the next sequence number (the unique index on `(chat_id, sequence)` catches a race).

### 2.10 The AG-UI run: `Features/AgUi/AgUiEndpoint.cs` and `EchoChatClient.cs`

`POST /agent` reads an AG-UI `RunAgentInput` with the AG-UI .NET SDK (`AGUI.Abstractions` 1.0) and writes AG-UI events as server-sent events (`TypedResults.ServerSentEvents`). It uses only the last user message; the history the model sees comes from the database; the thread id it answers with is the session's chat id, whatever the client sent. `EchoChatClient` implements `IChatClient` and streams `You said: ...` word by word.

### 2.11 Wiring: `Program.cs`, the AppHost and the database role

- `Program.cs` registers the options (`Agent:IdleTimeout`, `Agent:Gateway:ApiKey`, required outside Development), the `agent-db` context with migrations at startup, the sessions, the gateway client and the echo. It adds security headers (`Content-Security-Policy`, `nosniff`, `no-referrer`), the client-header check, the endpoints, and serves `wwwroot` with `index.html` as fallback.
- `core_banking/bank-platform/src/Bank.AppHost/AppHost.cs`: resource `agent`, with its own `agent-db` connection string, a reference to the gateway, external HTTP endpoint (port 5180).
- `core_banking/bank-platform/db/init/002-agent-role-and-schema.sql`: role and schema `agent`, created only when `AGENT_DB_PASSWORD` is set. The platform's role isolation tests now include `agent`: it cannot read or write any bank schema, and no bank role can read `agent`.

### 2.12 The chat UI: `agent/chat-ui/`

- `src/api.ts`: `login`, `logout`, `me` on `/auth`, always with `X-Bank-Chat: 1`; `noticeFor(code)` turns a code into the sentence the person reads.
- `src/LoginForm.tsx`: the login form as chat bubbles. Its values go only to `login()`, and both fields are emptied as soon as the call returns.
- `src/ChatView.tsx`: assistant-ui primitives (`ThreadPrimitive`, `MessagePrimitive`, `ComposerPrimitive`) on `useAgUiRuntime` with an `HttpAgent` for `/agent`. A `RUN_ERROR` with a session code, or an HTTP error that `me()` explains, sends the person back to the login.
- `src/App.tsx`: two screens, login and chat. It mounts `ChatView` with `key={chatId}`, so every login starts from an empty component: nothing of an earlier chat or of the login form survives in memory.
- `vite.config.ts`: builds into the host's `wwwroot`; in development, proxies `/auth` and `/agent` to port 5180.

---

## Part 3. Why it is designed this way

### 3.1 Majid's decisions (D1)

Phase design (workspace ADR 0008):

- **BFF with the token in the agent host** (A9). The only place the token exists outside identity is one server process. The price: the host is security-critical code, and a restart logs everyone out.
- **Chat history in PostgreSQL** (A10). Old chats can be read later. The price: personal data at rest, kept forever in this version.
- **A new chat per login** (A12). Whoever uses the browser next never sees the previous person's chat.

Stage 1 (agent ADR 0001):

- **assistant-ui** over CopilotKit. CopilotKit's normal setup puts a Node runtime between the browser and the agent; assistant-ui connects straight to our AG-UI endpoint, so the BFF stays in .NET. The price: fewer ready-made pieces than CopilotKit.
- **Host sessions in memory.** No secret on disk; identity's sessions do not survive a restart anyway. The price: one host instance only.
- **UI served by the host.** One origin, so `SameSite=Strict` cookies and no CORS. The price: UI and host deploy together.

### 3.2 Claude's choices (D2)

- **Our own AG-UI endpoint** on the stable AG-UI SDK types instead of Microsoft Agent Framework's preview `MapAGUIServer`. It lets the host check the session before the run, own the history, and ignore client-written turns. The choice for stage 2, when the real agent arrives, goes to Majid.
- **Only the last user message counts.** The client cannot inject earlier turns; a test sends a fake system message and a fake assistant reply and checks they never reach the database.
- **Every run asks identity.** It keeps the bank session alive while the person only chats, and it notices a login elsewhere at once.
- **`RUN_ERROR` for an ended session, HTTP 401 for none at all.** The UI gets the exact reason in the stream.
- **A custom header next to `SameSite=Strict`.** Two independent guards against cross-site requests.
- **Optional `002` init file** for the agent role, so deployments that do not run the agent yet keep working.

### 3.3 How the design changed while it was built

- The first version checked the session twice per run (once before the stream, once inside). It now checks once and passes the session into the stream.
- The first version deleted the user's other host sessions at a new login. The browser end-to-end test showed the old tab then said "nothing happened for a while" instead of "you logged in somewhere else". Now a replaced session is kept, marked, explains itself once and is removed, the same way identity handles `session_replaced`.
- A same-browser login as another user closed the old chat as `session_expired`; it now closes as `new_login` and also logs the old user out at identity.

### 3.4 Patterns used

- **Backend for frontend (BFF)** with **server-side sessions** and an **opaque, HttpOnly cookie**.
- **Defence in depth against CSRF**: `SameSite=Strict` plus a custom request header.
- **Server-owned conversation state**: the client is a view, not the source of truth.
- **Functional core, imperative shell**: pure `ChatAccess` and `SessionEnd`, thin endpoints around them.
- **Ports and adapters**: `IChatClient` for the model, `BankGateway` for the bank.
- **Streaming protocol (AG-UI over server-sent events)**: runs, typed events, error events.

---

## What was tested

| Project | Scenarios | Result |
| --- | --- | --- |
| `Bank.Agent.UnitTests` | chat access for customer, admin, closed chats; identity codes to reasons; idle edge | 23 passed |
| `Bank.Agent.IntegrationTests` (Testcontainers PostgreSQL, stub gateway) | migration and schema; login cookie flags; wrong and blank logins; bank down; me; login elsewhere (`session_replaced` once); same-browser second login; logout; host idle timeout; missing client header; echo run events and stored messages; run keeps the bank session alive; client history ignored; run after expiry or replacement; run without session; run without text; password and token never stored or logged | 24 passed |
| `chat-ui` (Vitest, jsdom) | login form sends and clears values; error and notice; hidden password; API header and codes; app shows the reason, opens a new chat without login traces, logs out | 14 passed |
| `chat-ui-e2e` (Playwright, Chromium, AppHost) | customer login → empty chat, no login trace, cookie not readable by scripts, echo, logout; second login tells the first tab why; admin chat; wrong password | 4 passed |
| `Bank.Platform.IntegrationTests` | agent healthy and migrated; `agent` role isolated from every bank schema | 54 passed |

Commands: `dotnet test` in `agent/` and in `core_banking/bank-platform`, `npm test` in `agent/chat-ui`, `npx playwright test` in `agent/tests/chat-ui-e2e` with the AppHost running.

## What to watch later

- The home bank does not run the agent yet. Its database predates `002`: create the `agent` role by hand when it moves there (hosting stage).
- One agent host instance; a restart logs everyone out.
- The UI bundle is about 1 MB minified; split it when it grows.
- Chats are kept forever with personal data.
- Stage 2 decides how Microsoft Agent Framework plugs into `/agent` (our endpoint, or `MapAGUIServer`).

## Check your understanding

1. Why does the agent host use only the last user message of a run, when the client sends the whole conversation?
2. What would an attacker on another website need to post to `/agent` successfully, and which two guards stop them?
3. Why does every run call identity's `sessions/current`, even though the echo never touches the bank?
4. A customer logs in on a laptop, then on a phone. What does the laptop show on its next message, and why does it not say "nothing happened for a while"?
5. Why is `ChatView` mounted with `key={chatId}`, and what could leak without it?
6. Where would the password be visible if the login form were a normal chat message to the model?
