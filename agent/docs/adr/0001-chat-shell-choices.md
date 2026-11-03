# 0001. Build the chat shell on assistant-ui, an in-memory session store and our own AG-UI endpoint

- Status: Accepted
- Date: 2026-11-03
- Scope: agent (Bank.Agent.Host, chat-ui), bank-platform
- Design plan reference: `phase2-design-plan.md` decisions A7, A9, A10, A12; workspace ADR 0008
- Decided by: Majid for 1–3 (stage 1 D1 decisions, 2026-11-03); Claude for 4–7 (D2, explained at the checkpoint).

## Context

Stage 1 of phase 2 builds the frame around the future agent: a login the model never sees, a bank token that never leaves the agent host, a new chat per login, and a clear end when the bank session ends. There is no language model yet; an echo stands in for it. Three library and hosting questions had to be answered first, and a few technical ones came up while building.

## Decision

1. **assistant-ui for the chat** (Majid). `@assistant-ui/react` with `@assistant-ui/react-ag-ui` and `@ag-ui/client` connect straight to the agent host's AG-UI endpoint. No Node runtime sits in between.
2. **Host sessions in memory** (Majid). `HostSessions` keeps the bank token, the principal and the open chat id per opaque cookie value. Nothing is written to disk; a restart logs everyone out.
3. **The UI is served by the agent host** (Majid). `npm run build` writes into the host's `wwwroot`; the page, `/auth` and `/agent` share one origin, so the cookie needs no CORS.
4. **Our own AG-UI endpoint on the AG-UI .NET SDK types** (Claude). `POST /agent` reads `RunAgentInput` and writes events with `AGUI.Abstractions` 1.0 (stable). Of the client's messages, only the last user message is used; the history comes from schema `agent`, and the run writes only to the session's open chat. The model sits behind `IChatClient` (`EchoChatClient` now). Whether stage 2 moves to Microsoft Agent Framework's `MapAGUIServer` (preview) is a D1 decision for that stage.
5. **Two guards against cross-site requests** (Claude). The cookie is `HttpOnly`, `Secure` and `SameSite=Strict`, and every state-changing call must carry `X-Bank-Chat: 1`, which a form on another site cannot add.
6. **A replaced session explains itself once** (Claude). A new login marks the user's other host sessions as ended instead of deleting them, so the old tab's next request gets `session_replaced` and then the session is gone, mirroring identity (workspace ADR 0007). The first end-to-end run showed the need: without it, the old tab said "nothing happened for a while".
7. **The `agent` database role is optional in init** (Claude). `db/init/002-agent-role-and-schema.sql` creates it only when `AGENT_DB_PASSWORD` is set. The AppHost always sets it; Docker Compose will from the hosting stage on. The six bank roles stay mandatory in `001`.

## Rationale

assistant-ui keeps the BFF and the security boundary in .NET, in one process. In-memory sessions add no secret at rest and lose nothing that identity does not lose anyway on a restart. Same-origin serving is the simplest setup where `SameSite=Strict` just works. Our own endpoint lets the host check the session before any run, own the history and refuse client-written turns; with no model yet, nothing in the preview hosting package was needed.

## Alternatives considered

- CopilotKit with CopilotRuntime: rejected; a Node service in the middle would have to carry the cookie to .NET. Its direct mode is marked development only.
- Only `@ag-ui/client` with hand-made components: rejected; the whole chat UI would be ours.
- Host sessions in PostgreSQL with Data Protection: rejected; a secret on disk, and identity's sessions die on restart anyway.
- A separate nginx container for the UI: rejected; needs a proxy or CORS with cross-origin cookies.
- `MapAGUIServer` in stage 1: postponed; preview package, and the client-sent history and thread ids would need extra guarding.
- Deleting replaced sessions at once: rejected after the end-to-end run, see 6.
- Adding `AGENT_DB_PASSWORD` to every compose file and the EC2 secret now: postponed to the hosting stage; the agent does not run there yet.

## Consequences

- The bundle is about 1 MB minified (280 kB gzip). Acceptable for a demo; code splitting can come later.
- `dotnet build` and `dotnet test` need no Node; publishing the host runs `npm ci && npm run build` first.
- The live home bank's database predates `002`: the role must be created by hand when the agent moves there.
- One host instance only, like identity.
